# Контракт Tool

## Общий интерфейс

Каждый Tool получает типизированную команду и контекст выполнения, а возвращает успешный результат либо ошибку в едином формате.

```csharp
public interface ITool<TCommand, TResult>
{
    Task<ToolResult<TResult>> ExecuteAsync(
        TCommand command,
        ToolContext context,
        CancellationToken cancellationToken);
}

public sealed record ToolContext(
    Guid OperationId,
    Guid? UserId,
    Guid? JobId = null,
    string? LeaseToken = null);

public abstract record ToolResult<TResult>
{
    public sealed record Success(TResult Value) : ToolResult<TResult>;

    public sealed record Failure(ToolError Error) : ToolResult<TResult>;
}

public sealed record ToolError(
    string Code,
    string Message,
    string? Field = null);
```

`OperationId` идентифицирует конкретный вызов Tool. Для обработки `BookingJob` контекст также содержит `JobId` и `LeaseToken`. Системные операции, например автоматическое завершение поездки, могут выполняться без `UserId` и без собственного `BookingJob`.

Tool не формирует HTTP-ответы и не выбрасывает исключения для ожидаемых бизнес-ошибок. Ожидаемая ошибка возвращается как `ToolResult.Failure`. Необработанное техническое исключение перехватывает Dispatcher и переводит связанный Job в `Failed`, если операция выполнялась для Job.

## Распределение проверок

| Участник | Что проверяет |
| --- | --- |
| `HTTP API` | Формат JSON, обязательные поля, типы значений, ограничения размера и наличие аутентификации. |
| `Dispatcher` | Наличие Tool в Registry, состояние Job, атомарный захват Job и актуальность `LeaseToken`. |
| `Tool` | Бизнес-правила, права пользователя, текущее состояние поездки или брони и конкурентные изменения данных. |
| Хранилище | Уникальные ограничения, транзакционность, блокировки и защиту данных от конфликтующих изменений. |

Ключ идемпотентности проверяет HTTP API до создания Job. Tool не создаёт второй Job и не обрабатывает `Idempotency-Key`.

## Общие правила

- Tool вызывается только Dispatcher.
- Одна команда обрабатывается одним Tool.
- Tool не зависит от HTTP и не возвращает HTTP-коды.
- Ожидаемые бизнес-ошибки возвращаются через `ToolResult.Failure`.
- Связанные изменения данных выполняются в одной транзакции.
- Для операции над `BookingJob` результат принимается только при актуальном `LeaseToken`.
- Коды ошибок Tool преобразуются HTTP API в единый формат ответа.

## BookingTool

`BookingTool` создаёт бронь или отменяет отдельную бронь.

### Входные данные

```csharp
public enum BookingOperation
{
    Create,
    CancelByPassenger,
    CancelByDriver
}

public sealed record BookingCommand(
    BookingOperation Operation,
    Guid TripId,
    Guid? BookingId,
    int? SeatCount,
    bool? BookingForOthers);
```

Для операции `Create` обязательны:

- `TripId`;
- `SeatCount`;
- `BookingForOthers`;
- `ToolContext.JobId`;
- `ToolContext.LeaseToken`.

Для отмены обязателен `BookingId`.

Операция `CancelByPassenger` доступна владельцу брони. Операция `CancelByDriver` доступна только владельцу поездки, к которой относится бронь.

### Результат

```csharp
public sealed record BookingResult(
    Guid BookingId,
    Guid TripId,
    string Status,
    int SeatCount,
    int RemainingSeats);
```

### Ошибки

| Код | Когда возвращается | Поле |
| --- | --- | --- |
| `TRIP_NOT_FOUND` | Поездка не существует. | `tripId` |
| `BOOKING_NOT_FOUND` | Бронь не существует. | `bookingId` |
| `TRIP_NOT_ACTIVE` | Поездка отменена или завершена. | `tripId` |
| `SELF_BOOKING` | Водитель пытается забронировать собственную поездку. | `tripId` |
| `INVALID_SEAT_COUNT` | Количество мест меньше единицы. | `seatCount` |
| `BOOKING_FOR_OTHERS_REQUIRED` | Запрошено несколько мест без отметки «бронирую другому». | `bookingForOthers` |
| `INSUFFICIENT_SEATS` | В поездке недостаточно свободных мест. | `seatCount` |
| `FORBIDDEN` | Пользователь не имеет права отменить бронь. | `bookingId` |
| `INVALID_BOOKING_STATE` | Бронь уже завершена или отменена. | `bookingId` |

### Транзакционность и конкурентные операции

При создании брони в одной транзакции выполняются:

1. блокировка выбранной поездки;
2. проверка состояния поездки;
3. проверка водителя и пассажира;
4. проверка количества свободных мест;
5. создание брони;
6. уменьшение количества свободных мест;
7. переход `BookingJob` из `Processing` в `Confirmed`.

Если транзакция не зафиксирована, ни одно из перечисленных изменений не сохраняется.

`BookingTool` и `TripCancellationTool` используют одну и ту же блокировку поездки. Поэтому одновременные бронирование и отмена поездки получают однозначный порядок:

- если первой фиксируется отмена поездки, бронирование возвращает `TRIP_NOT_ACTIVE`;
- если первой фиксируется бронь, последующая отмена поездки переводит созданную бронь в `CancelledBecauseTripCancelled`.

При отмене отдельной брони изменение её состояния и освобождение всех занятых ею мест также выполняются в одной транзакции.

## TripCompletionTool

`TripCompletionTool` завершает поездку вручную по команде водителя или автоматически после установленного времени.

### Входные данные

```csharp
public enum CompletionReason
{
    Manual,
    Automatic
}

public sealed record TripCompletionCommand(
    Guid TripId,
    CompletionReason Reason);
```

При ручном завершении `ToolContext.UserId` содержит идентификатор пользователя. Tool проверяет, что пользователь является водителем поездки.

При автоматическом завершении `UserId` отсутствует. Такая команда может поступить только от доверенного внутреннего компонента системы.

### Момент автоматического завершения

Момент автоматического завершения определяется по следующим правилам:

1. если указано `ArrivalTime`, поездку можно автоматически завершить через два часа после `ArrivalTime`;
2. если `ArrivalTime` отсутствует, используется `DepartureTime + 2 часа`;
3. если рассчитанный момент ещё не наступил, Tool возвращает `COMPLETION_TIME_NOT_REACHED`.

Если указаны оба времени, приоритет имеет `ArrivalTime`.

### Результат

```csharp
public sealed record TripCompletionResult(
    Guid TripId,
    string Status,
    DateTimeOffset CompletedAt,
    int CompletedBookingsCount);
```

### Ошибки

| Код | Когда возвращается | Поле |
| --- | --- | --- |
| `TRIP_NOT_FOUND` | Поездка не существует. | `tripId` |
| `FORBIDDEN` | Поездку вручную завершает не её водитель. | `tripId` |
| `INVALID_TRIP_STATE` | Поездка уже завершена или отменена. | `tripId` |
| `COMPLETION_TIME_NOT_REACHED` | Автоматическое завершение запрошено раньше разрешённого момента. | `tripId` |

### Транзакционность

При успешном выполнении Tool в одной транзакции:

1. блокирует поездку;
2. проверяет её текущее состояние;
3. переводит поездку в состояние `Completed`;
4. переводит связанные подтверждённые `BookingJob` из `Confirmed` в `Completed`;
5. фиксирует время завершения;
6. возвращает количество завершённых бронирований.

Если транзакция не зафиксирована, поездка и связанные бронирования сохраняют прежние состояния.

## Преобразование ошибки в HTTP

HTTP API преобразует ошибку Tool в единое тело:

```json
{
  "code": "INSUFFICIENT_SEATS",
  "message": "The trip does not have enough available seats",
  "field": "seatCount"
}
```

Соответствие ошибок HTTP-статусам зафиксировано в `docs/api/openapi.yaml`:

- ошибка входного поля — `400`;
- недостаток аутентификации — `401`;
- недостаток прав — `403`;
- отсутствие сущности — `404`;
- конфликт состояния или данных — `409`;
- неожиданная техническая ошибка — `500`;
- недоступность внешнего сервиса — `502`.
