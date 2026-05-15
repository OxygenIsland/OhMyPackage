using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 日志上报服务接口，供业务层注入使用。
    /// </summary>
    public interface ILogUploadService
    {
        UniTask<LogUploadService.UploadResult> UploadAsync(
            string uploadUrl,
            string token,
            string userId,
            string reason = "manual_feedback",
            CancellationToken cancellationToken = default);
    }
}
