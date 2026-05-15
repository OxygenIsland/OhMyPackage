using System.Threading;
using Cysharp.Threading.Tasks;
using MyGame.Toolkit.Network;

namespace GameFramework
{
    /// <summary>
    /// 可注入的日志上报门面，内部统一通过 IWebRequestService 发起上传。
    /// </summary>
    public sealed class LogUploadFacade : ILogUploadService
    {
        private readonly IWebRequestService _webRequestService;

        public LogUploadFacade(IWebRequestService webRequestService)
        {
            _webRequestService = webRequestService;
        }

        public UniTask<LogUploadService.UploadResult> UploadAsync(
            string uploadUrl,
            string token,
            string userId,
            string reason = "manual_feedback",
            CancellationToken cancellationToken = default)
        {
            return LogUploadService.UploadAsync(
                _webRequestService,
                uploadUrl,
                token,
                userId,
                reason,
                cancellationToken);
        }
    }
}
