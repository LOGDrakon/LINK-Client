using Link.Core.Frames;
using Link.Core.Parsing;

public class LinkChunkedParsingTests
{
    [Fact]
    public void Parse_FrameReceivedInMultipleChunks_Reassembles()
    {
        var parser = new LinkParser();
        LinkFrame? received = null;
        parser.FrameReceived += f => received = f;

        // Simulate a long frame split across 3 chunks
        var fullFrame = "LINK:DRAGON:AUTH:abcdefghij1234567890abcdefghij\0";
        int chunkSize = 16;

        for (int i = 0; i < fullFrame.Length; i += chunkSize)
        {
            int len = Math.Min(chunkSize, fullFrame.Length - i);
            parser.Feed(fullFrame.AsSpan(i, len));
        }

        Assert.NotNull(received);
        Assert.Equal("DRAGON", received!.AppId);
        Assert.Equal("AUTH", received.Command);
        Assert.Equal("abcdefghij1234567890abcdefghij", received.Arguments[0]);
    }

    [Fact]
    public void Parse_SingleByteChunks_Reassembles()
    {
        var parser = new LinkParser();
        LinkFrame? received = null;
        parser.FrameReceived += f => received = f;

        var fullFrame = "LINK:APP:CMD:ARG\0";

        foreach (char c in fullFrame)
            parser.Feed(new ReadOnlySpan<char>(in c));

        Assert.NotNull(received);
        Assert.Equal("APP", received!.AppId);
        Assert.Equal("CMD", received.Command);
        Assert.Equal("ARG", received.Arguments[0]);
    }

    [Fact]
    public void Parse_TwoFramesSplitAcrossChunks_BothReceived()
    {
        var parser = new LinkParser();
        var received = new List<LinkFrame>();
        parser.FrameReceived += f => received.Add(f);

        var data = "LINK:A:CMD1:LONGARG123456789\0LINK:B:CMD2:SHORT\0";
        int chunkSize = 10;

        for (int i = 0; i < data.Length; i += chunkSize)
        {
            int len = Math.Min(chunkSize, data.Length - i);
            parser.Feed(data.AsSpan(i, len));
        }

        Assert.Equal(2, received.Count);
        Assert.Equal("A", received[0].AppId);
        Assert.Equal("CMD1", received[0].Command);
        Assert.Equal("LONGARG123456789", received[0].Arguments[0]);
        Assert.Equal("B", received[1].AppId);
        Assert.Equal("CMD2", received[1].Command);
    }
}
