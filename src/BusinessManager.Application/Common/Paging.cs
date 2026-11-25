namespace BusinessManager.Application.Common;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int PageCount =>
        TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < PageCount;
}

public static class Paging
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    /// <summary>Clamps untrusted paging input to safe values.</summary>
    public static (int Page, int PageSize, int Skip) Normalize(int page, int pageSize)
    {
        int size = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        int number = Math.Max(1, page);

        // Prevent overflow when someone asks for an absurd page number.
        long skip = (long)(number - 1) * size;
        if (skip > int.MaxValue - size)
        {
            number = 1;
            skip = 0;
        }

        return (number, size, (int)skip);
    }
}
