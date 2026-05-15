namespace GameFramework.Resource
{
    internal class ResourceLogger : YooAsset.ILogger
    {
        private static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        public void Log(string message)
        {
            logger.Info(message);
        }

        public void Warning(string message)
        {
            logger.Warn(message);
        }

        public void Error(string message)
        {
            logger.Error(message);
        }

        public void Exception(System.Exception exception)
        {
            logger.Fatal(exception.Message);
        }
    }
}