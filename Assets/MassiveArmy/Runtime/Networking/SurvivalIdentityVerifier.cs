using System;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MassiveArmy.Networking {
    /// <summary>Verify UAS signatures and claims; never trust a player ID supplied by a client.</summary>
    internal sealed class SurvivalIdentityVerifier {
        [Serializable] sealed class Header {public string alg,kid;}
        [Serializable] sealed class Claims {public string sub,iss,project_id;public string[] aud;public long exp,nbf;}
        [Serializable] sealed class Key {public string kid,kty,alg,n,e;}
        [Serializable] sealed class Keys {public Key[] keys;}
        Keys cached;double fetched;Task refresh;
        static byte[] Decode(string s){s=s.Replace('-','+').Replace('_','/');return Convert.FromBase64String(s.PadRight((s.Length+3)/4*4,'='));}
        async Task Refresh(){
            for(int attempt=0;attempt<3;attempt++){
                using(var request=UnityWebRequest.Get("https://player-auth.services.api.unity.com/.well-known/jwks.json")){
                    request.timeout=5;var operation=request.SendWebRequest();while(!operation.isDone)await Task.Yield();
                    if(request.result==UnityWebRequest.Result.Success){
                        var result=JsonUtility.FromJson<Keys>(request.downloadHandler.text);if(result?.keys==null||result.keys.Length==0)throw new InvalidOperationException("Invalid authentication keys");
                        cached=result;fetched=Time.realtimeSinceStartupAsDouble;return;
                    }
                    if(request.responseCode>=400&&request.responseCode<500&&request.responseCode!=408&&request.responseCode!=429)throw new InvalidOperationException("Authentication key service rejected the request");
                }
                if(attempt<2)await Task.Delay(250*(1<<attempt));
            }
            throw new InvalidOperationException("Authentication keys unavailable after bounded retry");
        }
        public async Task Prepare(){if(cached!=null&&Time.realtimeSinceStartupAsDouble-fetched<=3600)return;if(refresh==null||refresh.IsCompleted)refresh=Refresh();await refresh;}
        public async Task<string> Verify(string token,string project,string environment){
            if(string.IsNullOrEmpty(token)||token.Length>8192)throw new InvalidOperationException("Missing authentication token");
            var parts=token.Split('.');if(parts.Length!=3)throw new InvalidOperationException("Invalid authentication token");
            var header=JsonUtility.FromJson<Header>(Encoding.UTF8.GetString(Decode(parts[0])));
            if(header==null||header.alg!="RS256")throw new InvalidOperationException("Unsupported authentication signature");
            if(cached==null||Time.realtimeSinceStartupAsDouble-fetched>3600){if(refresh==null||refresh.IsCompleted)refresh=Refresh();await refresh;}
            var key=Array.Find(cached.keys,k=>k.kid==header.kid&&k.kty=="RSA"&&k.alg=="RS256");
            if(key==null&&Time.realtimeSinceStartupAsDouble-fetched>30){if(refresh==null||refresh.IsCompleted)refresh=Refresh();await refresh;key=Array.Find(cached.keys,k=>k.kid==header.kid&&k.kty=="RSA"&&k.alg=="RS256");}
            if(key==null)throw new InvalidOperationException("Unknown authentication signing key");
            using(var rsa=RSA.Create()){
                rsa.ImportParameters(new RSAParameters{Modulus=Decode(key.n),Exponent=Decode(key.e)});
                if(!rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0]+"."+parts[1]),Decode(parts[2]),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))throw new InvalidOperationException("Invalid authentication signature");
            }
            var claims=JsonUtility.FromJson<Claims>(Encoding.UTF8.GetString(Decode(parts[1])));long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if(claims==null||string.IsNullOrEmpty(claims.sub)||claims.sub.Length>128||claims.iss!="https://player-auth.services.api.unity.com"||claims.project_id!=project||claims.exp<=now||claims.nbf>now+10||claims.aud==null||Array.IndexOf(claims.aud,"envName:"+environment)<0)throw new InvalidOperationException("Authentication claims do not match this server");
            return claims.sub;
        }
    }
}
