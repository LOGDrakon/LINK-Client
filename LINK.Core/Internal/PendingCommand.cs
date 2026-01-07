using Link.Core.Frames;

namespace Link.Core.Internal;

internal sealed class PendingCommand
{
    public string Command { get; }
    public TaskCompletionSource<LinkFrame> Tcs { get; }

    public PendingCommand(string command)
    {
        Command = command;
        Tcs = new TaskCompletionSource<LinkFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
