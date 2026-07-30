namespace Mocksmith.Core.Entities;

public class SampleTag
{
    public Guid SampleId { get; set; }
    public Sample? Sample { get; set; }

    public int TagId { get; set; }
    public Tag? Tag { get; set; }
}
