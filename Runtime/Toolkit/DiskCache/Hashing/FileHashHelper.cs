using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MyGame.Toolkit.DiskCache
{
    /// <summary>
    /// 文件 MD5 哈希计算工具。线程安全，可在任意线程调用。
    /// </summary>
    public static class FileHashHelper
    {
        private const int DefaultBufferSize = 81920; // 80 KB，兼顾内存与吞吐

        /// <summary>
        /// 同步计算文件的 MD5 哈希。
        /// </summary>
        /// <param name="filePath">文件绝对路径。</param>
        /// <returns>小写十六进制字符串（32 字符）。</returns>
        public static string ComputeMD5(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) {
                throw new ArgumentException("filePath must not be null or empty.", nameof(filePath));
            }
            if (!File.Exists(filePath)) {
                throw new FileNotFoundException("File not found for MD5 computation.", filePath);
            }

            using (var md5 = MD5.Create())
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, DefaultBufferSize)) {
                byte[] hash = md5.ComputeHash(stream);
                return BytesToHex(hash);
            }
        }

        /// <summary>
        /// 异步计算文件的 MD5 哈希（适合大文件，避免阻塞主线程）。
        /// </summary>
        /// <param name="filePath">文件绝对路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>小写十六进制字符串（32 字符）。</returns>
        public static async Task<string> ComputeMD5Async(string filePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(filePath)) {
                throw new ArgumentException("filePath must not be null or empty.", nameof(filePath));
            }
            if (!File.Exists(filePath)) {
                throw new FileNotFoundException("File not found for MD5 computation.", filePath);
            }

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (var md5 = MD5.Create())
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, DefaultBufferSize)) {
                    byte[] buffer = new byte[DefaultBufferSize];
                    int bytesRead;
                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0) {
                        cancellationToken.ThrowIfCancellationRequested();
                        md5.TransformBlock(buffer, 0, bytesRead, null, 0);
                    }
                    md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    return BytesToHex(md5.Hash);
                }
            }, cancellationToken);
        }

        /// <summary>
        /// 计算字节数组的 MD5 哈希。
        /// </summary>
        public static string ComputeMD5(byte[] data)
        {
            if (data == null) {
                throw new ArgumentNullException(nameof(data));
            }

            using (var md5 = MD5.Create()) {
                byte[] hash = md5.ComputeHash(data);
                return BytesToHex(hash);
            }
        }
        /// <summary>
        /// 计算字符串的 MD5 哈希（UTF-8 编码）。
        /// </summary>
        /// <param name="str">输入字符串。</param>
        /// <returns>小写十六进制字符串（32 字符）。</returns>
        public static string ComputeStringMD5(string str)
        {
            if (str == null) {
                throw new ArgumentNullException(nameof(str));
            }
            byte[] data = Encoding.UTF8.GetBytes(str);
            return ComputeMD5(data);
        }

        /// <summary>
        /// 验证文件的 MD5 是否与期望值一致。
        /// </summary>
        /// <param name="filePath">文件路径。</param>
        /// <param name="expectedMd5">期望的 MD5 哈希（大小写不敏感）。</param>
        /// <returns>匹配返回 true。</returns>
        public static bool Verify(string filePath, string expectedMd5)
        {
            if (string.IsNullOrEmpty(expectedMd5)) {
                return false;
            }

            try {
                string actual = ComputeMD5(filePath);
                return string.Equals(actual, expectedMd5, StringComparison.OrdinalIgnoreCase);
            }
            catch {
                return false;
            }
        }

        /// <summary>
        /// 异步验证文件 MD5。
        /// </summary>
        public static async Task<bool> VerifyAsync(string filePath, string expectedMd5, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(expectedMd5)) {
                return false;
            }

            try {
                string actual = await ComputeMD5Async(filePath, cancellationToken);
                return string.Equals(actual, expectedMd5, StringComparison.OrdinalIgnoreCase);
            }
            catch {
                return false;
            }
        }

        private static string BytesToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) {
                sb.Append(bytes[i].ToString("x2"));
            }
            return sb.ToString();
        }
    }
}
