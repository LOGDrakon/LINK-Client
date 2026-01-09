using Link.Core.Frames;
using Link.Core.Parsing;

public class LinkReturnTests
{
    [Fact]
    public void Parse_ReturnFrame()
    {
        var parser = new LinkParser();
        LinkFrame? frame = null;

        parser.FrameReceived += f => frame = f;
        parser.Feed("LINK:APP:RETURN:GETV:LINKv1.0\0");

        Assert.NotNull(frame);
        Assert.True(frame!.IsReturn);
        Assert.Equal("GETV", frame.ReturnedCommand);
        Assert.Equal("LINKv1.0", frame.ReturnArguments[0]);
    }

    [Fact]
    public void Return_WithoutArguments_IsHandled()
    {
        var frame = new LinkFrame("APP", "RETURN");

        Assert.True(frame.IsReturn);
        Assert.Null(frame.ReturnedCommand);
    }

}