using ReactiveUI.Fody.Helpers;

namespace ServiceLib.Models;

[Serializable]
public class ProfileItemModel : ProfileItem
{
    public bool IsActive { get; set; }
    public string SubRemarks { get; set; }
    public string ConfigTypeDisplay { get; set; } = string.Empty; // Display-friendly config type name

    [Reactive]
    public int Delay { get; set; }

    public decimal Speed { get; set; }
    public int Sort { get; set; }

    [Reactive]
    public string DelayVal { get; set; }

    [Reactive]
    public string SpeedVal { get; set; }

    [Reactive]
    public string TodayUp { get; set; }

    [Reactive]
    public string TodayDown { get; set; }

    [Reactive]
    public string TotalUp { get; set; }

    [Reactive]
    public string TotalDown { get; set; }
}
