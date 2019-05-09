namespace CryCrawler.Network
{
    public static class HttpUtils
    {
        public static (string, string, string) ParseRequest(string request)
        {
            var firstspace = request.IndexOf(' ');
            var secspace = request.IndexOf(' ', firstspace + 1);
            var method = request.Substring(0, firstspace).ToUpper();
            var url = request.Substring(firstspace + 1, secspace - (firstspace + 1));

            var body = "";
            var bindex = request.IndexOf("\n\n");
            if (bindex > 0) body = request.Substring(request.IndexOf("\n\n") + 2);
            return (method, url, body);
        }
    }
}
