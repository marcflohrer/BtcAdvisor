namespace BtcAdvisor.Repository.Models;

public partial class IndicatorError
{
    public int Id { get; set; }

    public string ErrorMessage { get; set; } = null!;

    public int IndicatorDataId { get; set; }

    public virtual IndicatorDatum IndicatorData { get; set; } = null!;
}
