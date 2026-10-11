using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Jellyfin.Plugin.HyperionGrabber.Core.Kodi;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Kodi;

public class JsonObjectSplitterTests
{
    [Fact]
    public void ObjectsBackToBack_InOneRead_AreSplit()
    {
        var objects = Split("{\"id\":1,\"result\":{}}{\"method\":\"Player.OnPause\"}");

        Assert.Equal(["{\"id\":1,\"result\":{}}", "{\"method\":\"Player.OnPause\"}"], objects);
    }

    [Fact]
    public void Object_ReadByteByByte_IsJoined()
    {
        const string Json = "{\"id\":2,\"result\":[{\"playerid\":1,\"type\":\"video\"}]}";

        var objects = Split(Json, chunk: 1);

        Assert.Equal([Json], objects);
    }

    [Theory]
    [InlineData("{\"label\":\"A } in the title {\"}")]
    [InlineData("{\"label\":\"Quote \\\" then }\"}")]
    [InlineData("{\"path\":\"C:\\\\Movies\\\\\",\"x\":[1,{\"y\":[]}]}")]
    public void BracesInStringsAndNestedArrays_DoNotEndTheObject(string json)
    {
        var objects = Split(json + json, chunk: 3);

        Assert.Equal([json, json], objects);
    }

    [Fact]
    public void WhitespaceBetweenObjects_IsSkipped()
    {
        var objects = Split("  {\"a\":1}\r\n {\"b\":2}\n");

        Assert.Equal(["{\"a\":1}", "{\"b\":2}"], objects);
    }

    [Fact]
    public void ManyObjectsInRandomChunks_AreAllFound()
    {
        var random = new Random(32);
        var expected = new List<string>();
        var stream = new StringBuilder();
        for (var i = 0; i < 200; i++)
        {
            var json = $"{{\"id\":{i},\"result\":{{\"label\":\"{new string('x', random.Next(0, 300))}}}\"}}}}";
            expected.Add(json);
            stream.Append(json);
        }

        var objects = Split(stream.ToString(), chunk: 0, random);

        Assert.Equal(expected, objects);
    }

    [Fact]
    public void ObjectLongerThanTheLimit_Throws()
    {
        var splitter = new JsonObjectSplitter(maxLength: 64);

        Assert.Throws<InvalidDataException>(() => Feed(splitter, "{\"label\":\"" + new string('x', 100) + "\"}"));
    }

    private static List<string> Split(string text, int chunk = 0, Random? random = null)
    {
        var splitter = new JsonObjectSplitter(maxLength: 1 << 20);
        var objects = new List<string>();
        var bytes = Encoding.UTF8.GetBytes(text);
        var offset = 0;
        while (offset < bytes.Length)
        {
            var count = Math.Min(chunk > 0 ? chunk : random?.Next(1, 700) ?? bytes.Length, bytes.Length - offset);
            var buffer = splitter.GetBuffer();
            count = Math.Min(count, buffer.Length);
            bytes.AsSpan(offset, count).CopyTo(buffer.Span);
            splitter.Advance(count);
            offset += count;
            while (splitter.TryRead(out var json))
            {
                objects.Add(Encoding.UTF8.GetString(json.Span));
            }
        }

        return objects;
    }

    private static void Feed(JsonObjectSplitter splitter, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var buffer = splitter.GetBuffer();
        bytes.CopyTo(buffer.Span);
        splitter.Advance(bytes.Length);
        while (splitter.TryRead(out _))
        {
            continue;
        }
    }
}
