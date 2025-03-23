namespace Scribal.Cli;

public interface IModelClient
{
    IAsyncEnumerable<string> GetResponse(string input);
}

public class ModelClient : IModelClient
{
    public IAsyncEnumerable<string> GetResponse(string input)
    {
        throw new NotImplementedException();
    }
}