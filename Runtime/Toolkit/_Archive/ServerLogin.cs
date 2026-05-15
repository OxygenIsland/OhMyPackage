// using System;
// using System.Collections.Generic;
// using System.Text;
// using Newtonsoft.Json;
// using UnityEngine;

// namespace StarWorld.Common.Utility
// {
//     public class ServerLogin
//     {
//         private string user;
//         private string sw;
//         private string verifyCode;

//         public ServerLogin(string user, string sw, string verifyCode)
//         {
//             this.user = user;
//             this.sw = sw;
//             this.verifyCode = verifyCode;
//         }

//         public class LoginData
//         {
//             public string username;

//             [JsonProperty("password")]
//             public string pw;
//             public string captchaCode;
//         }

//         public void Login(string url, Action<string> result)
//         {
//             var loginAuthInfo = new LoginData
//             {
//                 username = user,
//                 pw = sw,
//                 captchaCode = verifyCode,
//             };
//             string credential = JsonConvert.SerializeObject(loginAuthInfo);
//             byte[] post_data = Encoding.UTF8.GetBytes(credential);

//             var header = new Dictionary<string, string> { ["Content-Type"] = "application/json" };

//             WebService.GetWebRequestPostResponse(
//                 url,
//                 post_data,
//                 header,
//                 (dataString, webRequest) =>
//                 {
//                     JsonSerializerSettings setting = new JsonSerializerSettings
//                     {
//                         NullValueHandling = NullValueHandling.Ignore,
//                     };
//                     Dictionary<string, object> response = JsonConvert.DeserializeObject<
//                         Dictionary<string, object>
//                     >(webRequest.downloadHandler.text, setting);
//                     if (response.ContainsKey("code") && response["code"].ToString() == "200")
//                     {
//                         if (response.ContainsKey("data"))
//                         {
//                             string data = response["data"].ToString();
//                             result.Invoke(data);
//                             return;
//                         }
//                         result.Invoke(null);
//                         Debug.LogError("Login Failed " + response["code"]);
//                     }
//                 },
//                 (dataString, webRequest) =>
//                 {
//                     Debug.LogError("Login Failed " + webRequest.error);
//                     result.Invoke(null);
//                 }
//             );
//         }
//     }
// }
