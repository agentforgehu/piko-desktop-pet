namespace Piko.Context.Windows.Observation;

public interface IWindowsContextProbe
{
    WindowsContextSnapshot Capture(int idleThresholdSeconds = 120);
}
