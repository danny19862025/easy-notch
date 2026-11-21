// The shared Services/Weather.cs is written against WPF's DispatcherTimer
// (`using System.Windows.Threading;`). On Linux that name maps onto Avalonia's
// UI-thread timer, which has the same Interval / Tick / Start / Stop surface.
namespace System.Windows.Threading;

internal sealed class DispatcherTimer : Avalonia.Threading.DispatcherTimer
{
}
