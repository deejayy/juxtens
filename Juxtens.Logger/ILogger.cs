namespace Juxtens.Logger;

public interface ILogger
{
    void Info(string message);
    void Warning(string message);
    void Error(string message);
    void Error(string message, Exception ex);
}
