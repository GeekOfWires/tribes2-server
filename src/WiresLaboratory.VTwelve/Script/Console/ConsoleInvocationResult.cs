namespace WiresLaboratory.VTwelve.Script.Console;

/// <summary>
/// Outcome of dispatching a console command through <see cref="ConsoleRegistry"/>. The engine
/// does not throw on a bad call — an unknown command or a wrong argument count prints a console
/// error and the call evaluates to the empty string, and script execution continues.
/// <see cref="Success"/>/<see cref="Error"/> reproduce that shape in C# so a caller decides how
/// to surface the failure (log it, assert on it in a test, escalate it) instead of exceptions
/// driving normal script control flow the way the real engine never does.
/// </summary>
public readonly record struct ConsoleInvocationResult(bool Success, ConsoleValue Value, string? Error)
{
    public static ConsoleInvocationResult Ok(ConsoleValue value) => new(true, value, null);

    public static ConsoleInvocationResult Fail(string error) => new(false, ConsoleValue.Empty, error);
}
