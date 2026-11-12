namespace PedeAqui.Infrastructure.Redis;

public readonly record struct RedisOutcome<T>(bool Succeeded, T? Value)
{
    public static RedisOutcome<T> Success(T value) => new(true, value);

    public static RedisOutcome<T> Failure() => new(false, default);
}
