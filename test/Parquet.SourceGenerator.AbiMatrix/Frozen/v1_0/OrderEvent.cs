namespace SampleDomain.Models;

public sealed class OrderEvent
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public double Score { get; set; }

    public decimal Price { get; set; }

    public System.DateTime CreatedAt { get; set; }

    public System.TimeSpan Duration { get; set; }

    public System.Guid CorrelationId { get; set; }

    public System.Guid? OptionalGuid { get; set; }

    public byte[]? Payload { get; set; }
}
