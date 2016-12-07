﻿using System;
using System.Collections.Generic;

using YouduSDK.EntApp.AES;
using EasyHttp.Http;
using System.Net;
using YouduSDK.EntApp.Exceptions;
using JsonFx.Json;
using YouduSDK.EntApp.MessageEntity;
using System.IO;
using EasyHttp.Infrastructure;

namespace YouduSDK.EntApp
{
    public class AppClient
    {
        /// <summary>
        /// 文件类型
        /// </summary>
        public const string FileTypeFile = "file";

        /// <summary>
        /// 图片类型
        /// </summary>
        public const string FileTypeImage = "image";

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="address">目标服务器地址，"ip:port"的形式</param>
        /// <param name="buin">企业号</param>
        /// <param name="appId">AppId</param>
        /// <param name="encodingAesKey">encodingAesKey</param>
        public AppClient(string address, int buin, string appId, string encodingAesKey)
        {
            if (address.Length == 0 || buin == 0 || appId.Length == 0 || encodingAesKey.Length == 0)
            {
                throw new ArgumentException();
            }
            m_addr = address;
            m_buin = buin;
            m_appId = appId;
            m_crypto = new AESCrypto(appId, encodingAesKey);
        }

        private class Token
        {
            public string token;
            public long activeTime;
            public int expire;

            public Token(string token, long activeTime, int expire)
            {
                this.token = token;
                this.activeTime = activeTime;
                this.expire = expire;
            }
        }

        private AESCrypto m_crypto;
        private Token m_tokenInfo;

        private string m_addr;
        private int m_buin;
        private string m_appId;

        public string Addr
        {
            get
            {
                return m_addr;
            }
        }

        public int Buin
        {
            get
            {
                return m_buin;
            }
        }

        public string AppId
        {
            get
            {
                return m_appId;
            }
        }

        private object tokenQuery()
        {
            return new { accessToken = m_tokenInfo.token };
        }

        private string apiGetToken()
        {
            return EntAppApi.SCHEME + m_addr + EntAppApi.API_GET_TOKEN;
        }

        private string apiSendMsg()
        {
            return EntAppApi.SCHEME + m_addr + EntAppApi.API_SEND_MSG;
        }

        private string apiUploadFile()
        {
            return EntAppApi.SCHEME + m_addr + EntAppApi.API_UPLOAD_FILE;
        }

        private string apiDownloadFile()
        {
            return EntAppApi.SCHEME + m_addr + EntAppApi.API_DOWNLOAD_FILE;
        }

        private Token getToken()
        {
            try
            {
                var now = (long)new TimeSpan(new DateTime(1970, 1, 1).Ticks).TotalSeconds;
                var timestamp = AESCrypto.ToBytes(string.Format("%d", now));
                var encryptTime = m_crypto.Encrypt(timestamp);
                var param = new Dictionary<string, object>()
                {
                    { "buin",  m_buin },
                    { "appId", m_appId },
                    { "encrypt" , encryptTime }
                };
                var client = new HttpClient();
                var rsp = client.Post(this.apiGetToken(), param, HttpContentTypes.ApplicationJson);
                Helper.CheckHttpStatus(rsp);
                var body = rsp.StaticBody<Dictionary<string, object>>(overrideContentType: HttpContentTypes.ApplicationJson);
                Helper.CheckApiStatus(body);
                var encrypt = Helper.GetEncryptJsonValue(body);
                var buffer = m_crypto.Decrypt(encrypt);
                var tokenInfo = new JsonReader().Read<Dictionary<string, object>>(AESCrypto.ToString(buffer));
                object accessToken;
                object expireIn;
                if (!tokenInfo.TryGetValue("accessToken", out accessToken)
                    || accessToken is string == false
                    || ((string)accessToken).Length == 0
                    || !tokenInfo.TryGetValue("expireIn", out expireIn)
                    || expireIn is int == false
                    || (int)expireIn <= 0)
                {
                    throw new ParamParserException("invalid token or expireIn", null);
                }
                return new Token((string)accessToken, now, (int)expireIn);
            }
            catch (WebException e)
            {
                throw new HttpRequestException(0, e.Message, e);
            }
            catch (Exception e)
            {
                if (e is GeneralEntAppException)
                {
                    throw e;
                }
                else
                {
                    throw new UnexpectedException(e.Message, e);
                }
            }
        }

        private void checkAndRefreshToken()
        {
            if (m_tokenInfo == null) {
                m_tokenInfo = this.getToken();
            }
            long endTime = m_tokenInfo.activeTime + m_tokenInfo.expire;
            if (endTime <= (long)new TimeSpan(new DateTime(1970, 1, 1).Ticks).TotalSeconds) {
                m_tokenInfo = this.getToken();
            }
        }

        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="msg">Message对象</param>
        /// <exception cref="AESCryptoException">加解密失败</exception>
        /// <exception cref="ParamParserException">参数解析失败</exception>
        /// <exception cref="HttpRequestException">http请求失败</exception>
        /// <exception cref="UnexpectedException">其它可能的错误</exception>
        public void SendMsg(Message msg)
        {
            this.checkAndRefreshToken();

            try
            {
                Console.WriteLine(msg.ToJson());
                var cipherText = m_crypto.Encrypt(AESCrypto.ToBytes(msg.ToJson()));
                var param = new Dictionary<string, object>()
                {
                    { "buin", m_buin },
                    { "appId", m_appId },
                    { "encrypt", cipherText }
                };
                var client = new HttpClient();
                var rsp = client.Post(this.apiSendMsg(), param, HttpContentTypes.ApplicationJson, query: this.tokenQuery());
                Helper.CheckHttpStatus(rsp);
                var body = rsp.StaticBody<Dictionary<string, object>>(overrideContentType: HttpContentTypes.ApplicationJson);
                Helper.CheckApiStatus(body);
            }
            catch (WebException e)
            {
                throw new HttpRequestException(0, e.Message, e);
            }
            catch (Exception e)
            {
                if (e is GeneralEntAppException)
                {
                    throw e;
                }
                else
                {
                    throw new UnexpectedException(e.Message, e);
                }
            }
        }

        /// <summary>
        /// 上传文件
        /// </summary>
        /// <param name="type">文件类型</param>
        /// <param name="name">文件名称</param>
        /// <param name="path">文件路径</param>
        /// <returns>文件的资源ID</returns>
        /// <exception cref="AESCryptoException">加解密失败</exception>
        /// <exception cref="ParamParserException">参数解析失败</exception>
        /// <exception cref="HttpRequestException">http请求失败</exception>
        /// <exception cref="UnexpectedException">其它可能的错误</exception>
        public string UploadFile(string type, string name, string path)
        {
            this.checkAndRefreshToken();

            try
            {
                var encryptFile = m_crypto.Encrypt(Helper.ReadStream(new FileStream(path, FileMode.Open, FileAccess.Read)));
                var fileInfo = new { type = type, name = name };
                var cipherFileInfo = m_crypto.Encrypt(AESCrypto.ToBytes(new JsonWriter().Write(fileInfo)));

                var tempFileName = Path.GetTempFileName();
                Helper.WriteStream(new FileStream(tempFileName, FileMode.Open, FileAccess.Write), AESCrypto.ToBytes(encryptFile));

                var formData = new Dictionary<string, object>();
                formData["encrypt"] = cipherFileInfo;

                var fileData = new FileData();
                fileData.ContentType = HttpContentTypes.TextPlain;
                fileData.FieldName = "file";
                fileData.Filename = tempFileName;
                var fileList = new List<FileData>();
                fileList.Add(fileData);

                var client = new HttpClient();
                var rsp = client.Post(this.apiUploadFile(), formData, fileList, query: this.tokenQuery());
                File.Delete(tempFileName);

                Helper.CheckHttpStatus(rsp);
                var body = rsp.StaticBody<Dictionary<string, object>>(overrideContentType: HttpContentTypes.ApplicationJson);
                Helper.CheckApiStatus(body);

                var decryptBuffer = m_crypto.Decrypt(Helper.GetEncryptJsonValue(body));
                var mediaInfo = new JsonReader().Read<Dictionary<string, string>>(AESCrypto.ToString(decryptBuffer));
                string mediaId;
                if (!mediaInfo.TryGetValue("mediaId", out mediaId)
                    || mediaId.Length == 0)
                {
                    throw new ParamParserException("invalid mediaId", null);
                }
                return mediaId;
            }
            catch(IOException e)
            {
                throw new HttpRequestException(0, e.Message, e);
            }
            catch (WebException e)
            {
                throw new HttpRequestException(0, e.Message, e);
            }
            catch (Exception e)
            {
                if (e is GeneralEntAppException)
                {
                    throw e;
                }
                else
                {
                    throw new UnexpectedException(e.Message, e);
                }
            }
        }

        /// <summary>
        /// 下载文件
        /// </summary>
        /// <param name="mediaId">文件的资源ID</param>
        /// <param name="destDir">目标存储目录</param>
        /// <returns>(文件名, 文件字节数大小, 文件内容)</returns>
        /// <exception cref="AESCryptoException">加解密失败</exception>
        /// <exception cref="ParamParserException">参数解析失败</exception>
        /// <exception cref="HttpRequestException">http请求失败</exception>
        /// <exception cref="UnexpectedException">其它可能的错误</exception>
        public Tuple<String, long, byte[]> DownloadFile(string mediaId, string destDir)
        {
            this.checkAndRefreshToken();

            try
            {
                var mediaInfo = new Dictionary<string, string>()
                {
                    { "mediaId", mediaId }
                };
                var cipherMediaInfo = m_crypto.Encrypt(AESCrypto.ToBytes(new JsonWriter().Write(mediaInfo)));
                var param = new Dictionary<string, object>()
                {
                    { "buin", m_buin },
                    { "encrypt", cipherMediaInfo }
                };
                var client = new HttpClient();
                client.StreamResponse = true;
                var rsp = client.Post(this.apiDownloadFile(), param, HttpContentTypes.ApplicationJson, this.tokenQuery());

                Helper.CheckHttpStatus(rsp);

                var encryptFileInfo = rsp.RawHeaders["encrypt"];
                if (encryptFileInfo == null)
                {
                    var body = new JsonReader().Read<Dictionary<string, object>>(AESCrypto.ToString(Helper.ReadStream(rsp.ResponseStream)));
                    Helper.CheckApiStatus(body);
                    throw new ParamParserException("encrypt content not exists", null);
                }

                var decryptFileInfo = m_crypto.Decrypt(encryptFileInfo);
                var fileInfo = new JsonReader().Read<Dictionary<string, object>>(AESCrypto.ToString(decryptFileInfo));
                object name;
                object size;
                if (!fileInfo.TryGetValue("name", out name)
                    || name is string == false
                    || ((string)name).Length == 0
                    || !fileInfo.TryGetValue("size", out size)
                    || ((size is int && (int)size > 0) == false && (size is long && (long)size > 0) == false))
                {
                    throw new ParamParserException("invalid file name or size", null);
                }

                var encryptBuffer = Helper.ReadStream(rsp.ResponseStream);
                var decryptFile = m_crypto.Decrypt(AESCrypto.ToString(encryptBuffer));
                Helper.WriteStream(new FileStream(Path.Combine(destDir, (string)name), FileMode.OpenOrCreate, FileAccess.Write), decryptFile);
                return new Tuple<string, long, byte[]>((string)name, Convert.ToInt64(size), decryptFile);
            }
            catch (IOException e)
            {
                throw new HttpRequestException(0, e.Message, e);
            }
            catch (WebException e)
            {
                throw new HttpRequestException(0, e.Message, e);
            }
            catch (Exception e)
            {
                if (e is GeneralEntAppException)
                {
                    throw e;
                }
                else
                {
                    throw new UnexpectedException(e.Message, e);
                }
            }
        }
    }
}