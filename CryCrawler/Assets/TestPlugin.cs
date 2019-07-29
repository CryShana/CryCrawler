using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;

public class TestPlugin : Plugin
{
    public TestPlugin(Configuration config)
    {
        // called when plugin is loaded
    }

    public override void Dispose()
    {
        // called when program is stopped
    }

    public override void Info()
    {
        // Use Logger static class to log messages
        // as second parameter you can specify severity
        // Logger.LogSeverity.Debug
        // Logger.LogSeverity.Information
        // Logger.LogSeverity.Warning
        // Logger.LogSeverity.Error

        Logger.Log("TestPlugin [v1.0.0] - This is a test plugin - It works");
        Logger.Log("A debug message from plugin", Logger.LogSeverity.Debug);
    }

    public override void OnDump()
    {
        // called every time backlog gets dumped to cache or backup cache
    }

    public override void OnClientConnect(TcpClient client, string id)
    {
        // called every time a client successfully connects
    }

    public override bool OnClientConnecting(TcpClient client)
    {
        // called before client connects, return false to reject client
        return true;
    }

    public override void OnClientDisconnect(TcpClient client, string id)
    {
        // called on client disconnect
    }

    public override void OnDisconnect()
    {
        // called on disconnect from host
    }

    public override void OnConnect(string id)
    {
        // called on successful connection to host
    }

    public override bool BeforeDownload(string url, string destination)
    {
        // called before starting file download (from url to destination)
        // return false to reject download
        return true;
    }

    public override void AfterDownload(string url, string destination)
    {
        // called after a file is downloaded       
    }

    public override bool OnWorkReceived(string url)
    {
        // called every time crawler or worker manager gets next work to crawl/assign
        // return false to ignore this work
        return true;
    }

    // UNCOMMENT THIS FUNCTION TO USE IT
    /* 
    public IEnumerable<string> FindUrls(string url, string content) 
    {
        // this function overrides the default FindUrls method that Crawler uses for extracting URLs out of page content

        // this will extract domain name from URL and return the protocol (http/https)
        var domain = Extensions.GetDomainName(url, out string protocol);

        // use ' yield return "url" ' to return found urls
        for (int i = 0; i < 10; i++) yield return "some url";

        // yield break
    }    
    */
}