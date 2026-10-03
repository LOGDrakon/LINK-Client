using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Link.Core.Frames;

public class LinkFrameTests
{
    [Fact]
    public void ToString_BuildsValidFrame()
    {
        var frame = new LinkFrame("DRAGON", "GETV");
        Assert.Equal("LINK\u001fDRAGON\u001fGETV\0", frame.ToString());
    }
}
