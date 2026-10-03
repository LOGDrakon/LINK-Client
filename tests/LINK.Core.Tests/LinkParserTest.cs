using Link.Core.Parsing;
using Link.Core.Frames;

public class LinkParserTests
{
    [Fact]
    public void Parse_ValidFrame_RaisesEvent()
    {
        var parser = new LinkParser();
        LinkFrame? received = null;

        parser.FrameReceived += f => received = f;
        parser.Feed("LINK\u001fDRAGON\u001fGETV\0");

        Assert.NotNull(received);
        Assert.Equal("DRAGON", received!.AppId);
        Assert.Equal("GETV", received.Command);
    }

    [Fact]
    public void Parse_FrameWithArguments()
    {
        var parser = new LinkParser();
        LinkFrame? received = null;

        parser.FrameReceived += f => received = f;
        parser.Feed("LINK\u001fAPP\u001fCMD\u001fARG1\u001fARG2\0");

        Assert.Equal(2, received!.Arguments.Count);
        Assert.Equal("ARG1", received.Arguments[0]);
    }

    [Fact]
    public void Parse_PartialFrame_Works()
    {
        var parser = new LinkParser();
        LinkFrame? received = null;

        parser.FrameReceived += f => received = f;

        parser.Feed("LINK\u001fDRA");
        Assert.Null(received);

        parser.Feed("GON:GETV\0");
        Assert.NotNull(received);
    }

    [Fact]
    public void Parse_MultipleFrames()
    {
        var parser = new LinkParser();
        int count = 0;

        parser.FrameReceived += _ => count++;

        parser.Feed("LINK\u001fA\u001fCMD\0LINK\u001fB\u001fCMD\0");
        Assert.Equal(2, count);
    }

    [Fact]
    public void Parse_InvalidFrame_Ignored()
    {
        var parser = new LinkParser();
        bool called = false;

        parser.FrameReceived += _ => called = true;

        parser.Feed("INVALID:DATA\0");
        Assert.False(called);
    }
}
