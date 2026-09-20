using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace UniversalScanner.DeviceSDK.Honeywell
{
    /// <summary>
    /// Older Honeywell OEM models such as HBW4PER1 expose a Dahua-style first-run
    /// web UI, but they are intentionally kept out of the Dahua activator so
    /// working Dahua behavior remains unchanged.
    /// </summary>
    public static class HoneywellLegacyActivator
    {
        private const string LegacyAjaxContentType = "application/x-www-form-urlencoded; charset=UTF-8";

        public static bool ActivateDevice(
            string ipAddress,
            string username,
            string newPassword,
            out string message)
        {
            message = "";

            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                message = "IP address is empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(username))
                username = "admin";

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                message = "Password is empty.";
                return false;
            }

            if (newPassword.Length < 8)
            {
                message = "Honeywell legacy password must be at least 8 characters.";
                return false;
            }

            SecurityProtocolType prevProtocol = ServicePointManager.SecurityProtocol;
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 |
                SecurityProtocolType.Tls11 |
                SecurityProtocolType.Tls;

            System.Net.Security.RemoteCertificateValidationCallback prevCallback =
                ServicePointManager.ServerCertificateValidationCallback;
            ServicePointManager.ServerCertificateValidationCallback = (s, c, ch, e) => true;

            bool prevExpect100 = ServicePointManager.Expect100Continue;
            ServicePointManager.Expect100Continue = false;

            StringBuilder debug = new StringBuilder();
            string error = "";

            try
            {
                CookieContainer loginCookies = new CookieContainer();
                PreloadRoot("https://" + ipAddress + "/", loginCookies, debug);

                bool winHttpReachedLogin;
                if (TryDefaultPasswordChangeWinHttp(ipAddress, username, newPassword, debug, out error, out winHttpReachedLogin))
                {
                    string path = WriteDebugFile(ipAddress, "SUCCESS", debug.ToString());
                    message = "Honeywell legacy default password changed successfully." + FormatDebugPath(path);
                    return true;
                }

                if (winHttpReachedLogin && !IsConnectionClosedError(error))
                {
                    string path = WriteDebugFile(ipAddress, "FAILED", debug.ToString());
                    message =
                        "Honeywell legacy WinHTTP login succeeded, but the password-change RPC failed. " +
                        error +
                        "\r\n\r\nStopped before other fallbacks because this older Honeywell is already using the browser login/change-password flow." +
                        FormatDebugPath(path);
                    return false;
                }

                if (TryDefaultPasswordChange(ipAddress, username, newPassword, loginCookies, debug, out error))
                {
                    string path = WriteDebugFile(ipAddress, "SUCCESS", debug.ToString());
                    message = "Honeywell legacy default password changed successfully." + FormatDebugPath(path);
                    return true;
                }

                if (IsInvalidPasswordOrLockRisk(error))
                {
                    string path = WriteDebugFile(ipAddress, "FAILED", debug.ToString());
                    message =
                        "Honeywell legacy default login was rejected. " +
                        error +
                        "\r\n\r\nStopped before any further login attempts to avoid locking the camera." +
                        FormatDebugPath(path);
                    return false;
                }

                if ((error.IndexOf("password change", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     error.IndexOf("Security.getEncryptInfo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     error.IndexOf("modifyPassword", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     error.IndexOf("logged-in", StringComparison.OrdinalIgnoreCase) >= 0) &&
                    !IsConnectionClosedError(error))
                {
                    string path = WriteDebugFile(ipAddress, "FAILED", debug.ToString());
                    message =
                        "Honeywell legacy login succeeded, but the logged-in password-change RPC failed. " +
                        error +
                        "\r\n\r\nStopped before DevInit fallback because this older Honeywell is already using the login/change-password flow." +
                        FormatDebugPath(path);
                    return false;
                }

                if (IsConnectionClosedError(error))
                    AppendDebug(debug, "Honeywell legacy logged-in RPC connection closed", "https://" + ipAddress + "/RPC2", 0, "Continuing to isolated DevInit/OutsideCmd fallback.", error, null);

                string[] endpoints =
                {
                    "https://" + ipAddress + "/OutsideCmd",
                    "http://" + ipAddress + "/OutsideCmd"
                };

                foreach (string endpoint in endpoints)
                {
                    CookieContainer cookies = new CookieContainer();
                    PreloadRoot(endpoint, cookies, debug);

                    if (TryActivateEndpoint(endpoint, ipAddress, username, newPassword, cookies, debug, out error))
                    {
                        string path = WriteDebugFile(ipAddress, "SUCCESS", debug.ToString());
                        message = "Honeywell legacy device initialized successfully." + FormatDebugPath(path);
                        return true;
                    }
                }

                string debugPath = WriteDebugFile(ipAddress, "FAILED", debug.ToString());
                message =
                    "Honeywell legacy initialization failed. " +
                    error +
                    FormatDebugPath(debugPath) +
                    "\r\n\r\nThis path is isolated from Dahua activation and from HC30/HC35 Honeywell activation.";
                return false;
            }
            finally
            {
                ServicePointManager.ServerCertificateValidationCallback = prevCallback;
                ServicePointManager.SecurityProtocol = prevProtocol;
                ServicePointManager.Expect100Continue = prevExpect100;
            }
        }

        private static bool TryDefaultPasswordChangeWinHttp(
            string ipAddress,
            string username,
            string newPassword,
            StringBuilder debug,
            out string error,
            out bool reachedLogin)
        {
            error = "";
            reachedLogin = false;

            string oldPassword = "1234";
            string loginUrl = "https://" + ipAddress + "/RPC2_Login";
            string rpcUrl = "https://" + ipAddress + "/RPC2";

            try
            {
                Type winHttpType = Type.GetTypeFromProgID("WinHttp.WinHttpRequest.5.1");
                if (winHttpType == null)
                {
                    error = "WinHTTP COM object is not available.";
                    AppendDebug(debug, "Honeywell legacy WinHTTP transport", "https://" + ipAddress + "/", 0, null, error, null);
                    return false;
                }

                object request = Activator.CreateInstance(winHttpType);
                try
                {
                    string firstBody =
                        "{\"method\":\"global.login\"," +
                        "\"params\":{\"userName\":\"" + JsonEscape(username) + "\",\"password\":\"\",\"clientType\":\"Web3.0\",\"loginType\":\"Direct\"}," +
                        "\"id\":1}";

                    string firstResponse;
                    HttpStatusCode status;
                    string sendError;
                    if (!SendWinHttpRequest(request, loginUrl, "POST", firstBody, out firstResponse, out status, out sendError))
                    {
                        error = "WinHTTP first login failed: " + sendError;
                        AppendDebug(debug, "Honeywell legacy WinHTTP firstLogin", loginUrl, status, firstResponse, sendError, firstBody);
                        return false;
                    }

                    AppendDebug(debug, "Honeywell legacy WinHTTP firstLogin", loginUrl, status, firstResponse, null, firstBody);

                    string session = ExtractJsonString(firstResponse, "session") ?? ExtractJsonNumber(firstResponse, "session");
                    string encryption = ExtractJsonString(firstResponse, "encryption");
                    string random = ExtractJsonString(firstResponse, "random");
                    string realm = ExtractJsonString(firstResponse, "realm");
                    string authPassword = BuildLoginAuth(username, oldPassword, encryption, random, realm);
                    string sessionPart = BuildSessionPart(session);

                    string secondBody =
                        "{\"method\":\"global.login\"," +
                        "\"params\":{\"userName\":\"" + JsonEscape(username) + "\"," +
                        "\"password\":\"" + JsonEscape(authPassword) + "\"," +
                        "\"clientType\":\"Web3.0\"," +
                        "\"loginType\":\"Direct\"," +
                        "\"authorityType\":\"" + JsonEscape(encryption) + "\"}," +
                        "\"id\":2" + sessionPart + "}";

                    string secondResponse;
                    if (!SendWinHttpRequest(request, loginUrl, "POST", secondBody, out secondResponse, out status, out sendError))
                    {
                        error = "WinHTTP second login failed: " + sendError;
                        AppendDebug(debug, "Honeywell legacy WinHTTP secondLogin", loginUrl, status, secondResponse, sendError, secondBody);
                        return false;
                    }

                    AppendDebug(debug, "Honeywell legacy WinHTTP secondLogin", loginUrl, status, secondResponse, null, secondBody);
                    if (!IsRpcSuccess(status, secondResponse))
                    {
                        error = "WinHTTP second login rejected: " + DescribeRpcFailure(status, secondResponse);
                        string remaining = ExtractJsonNumber(secondResponse, "remainLoginTimes");
                        if (!string.IsNullOrWhiteSpace(remaining))
                            error += " (remaining login tries: " + remaining + ")";
                        return false;
                    }

                    reachedLogin = true;
                    session = ExtractJsonString(secondResponse, "session") ?? ExtractJsonNumber(secondResponse, "session") ?? session;
                    string loggedInSessionPart = BuildSessionPart(session);

                    string notifyResponse;
                    if (SendWinHttpRequest(request, "https://" + ipAddress + "/RPC2_Notify_Method", "GET", null, out notifyResponse, out status, out sendError))
                        AppendDebug(debug, "Honeywell legacy WinHTTP notify method", "https://" + ipAddress + "/RPC2_Notify_Method", status, notifyResponse, null, null);
                    else
                        AppendDebug(debug, "Honeywell legacy WinHTTP notify method", "https://" + ipAddress + "/RPC2_Notify_Method", status, notifyResponse, sendError, null);

                    string warmupBody = "{\"method\":\"system.listMethod\",\"params\":null,\"id\":3" + loggedInSessionPart + "}";
                    string warmupResponse;
                    if (SendWinHttpRequest(request, rpcUrl, "POST", warmupBody, out warmupResponse, out status, out sendError))
                        AppendDebug(debug, "Honeywell legacy WinHTTP system.listMethod", rpcUrl, status, warmupResponse, null, warmupBody);
                    else
                        AppendDebug(debug, "Honeywell legacy WinHTTP system.listMethod", rpcUrl, status, warmupResponse, sendError, warmupBody);

                    string serial = null;
                    if (!string.IsNullOrWhiteSpace(realm) &&
                        realm.StartsWith("Login to ", StringComparison.OrdinalIgnoreCase))
                        serial = realm.Substring("Login to ".Length);

                    if (string.IsNullOrWhiteSpace(serial))
                    {
                        string serialBody = "{\"method\":\"magicBox.getSerialNo\",\"params\":null,\"id\":4" + loggedInSessionPart + "}";
                        string serialResponse;
                        if (SendWinHttpRequest(request, rpcUrl, "POST", serialBody, out serialResponse, out status, out sendError))
                        {
                            AppendDebug(debug, "Honeywell legacy WinHTTP serial", rpcUrl, status, serialResponse, null, serialBody);
                            serial = ExtractJsonString(serialResponse, "sn") ??
                                     ExtractJsonString(serialResponse, "serial") ??
                                     ExtractJsonString(serialResponse, "result");
                        }
                        else
                        {
                            AppendDebug(debug, "Honeywell legacy WinHTTP serial", rpcUrl, status, serialResponse, sendError, serialBody);
                        }
                    }

                    if (string.IsNullOrWhiteSpace(serial))
                    {
                        error = "WinHTTP serial lookup did not return serial number.";
                        return false;
                    }

                    string encryptInfoBody = "{\"method\":\"Security.getEncryptInfo\",\"params\":null,\"id\":5" + loggedInSessionPart + "}";
                    string encryptInfoResponse;
                    if (!SendWinHttpRequest(request, rpcUrl, "POST", encryptInfoBody, out encryptInfoResponse, out status, out sendError))
                    {
                        error = "WinHTTP Security.getEncryptInfo failed: " + sendError;
                        AppendDebug(debug, "Honeywell legacy WinHTTP Security.getEncryptInfo", rpcUrl, status, encryptInfoResponse, sendError, encryptInfoBody);
                        return false;
                    }

                    AppendDebug(debug, "Honeywell legacy WinHTTP Security.getEncryptInfo", rpcUrl, status, encryptInfoResponse, null, encryptInfoBody);
                    if (!IsRpcSuccess(status, encryptInfoResponse))
                    {
                        error = "WinHTTP Security.getEncryptInfo rejected: " + DescribeRpcFailure(status, encryptInfoResponse);
                        return false;
                    }

                    string publicKey = ExtractJsonString(encryptInfoResponse, "pub");
                    string cipherHint = ExtractJsonString(encryptInfoResponse, "cipher");
                    if (string.IsNullOrWhiteSpace(publicKey))
                    {
                        error = "WinHTTP Security.getEncryptInfo did not return a public key.";
                        return false;
                    }

                    string passwordJson =
                        "{\"name\":\"" + JsonEscape(username) + "\"," +
                        "\"pwd\":\"" + JsonEscape(newPassword) + "\"," +
                        "\"pwdOld\":\"" + JsonEscape(oldPassword) + "\"}";

                    EncryptedPayload payload = EncryptPayload(publicKey, cipherHint, passwordJson);
                    string modifyBody =
                        "{\"method\":\"Security.modifyPasswordByType\"," +
                        "\"params\":{\"salt\":\"" + JsonEscape(payload.Salt) + "\"," +
                        "\"cipher\":\"AES-128\"," +
                        "\"content\":\"" + JsonEscape(payload.Content) + "\"}," +
                        "\"id\":6" + loggedInSessionPart + "}";

                    string modifyResponse;
                    if (!SendWinHttpRequest(request, rpcUrl, "POST", modifyBody, out modifyResponse, out status, out sendError))
                    {
                        error = "WinHTTP encrypted password change failed: " + sendError;
                        AppendDebug(debug, "Honeywell legacy WinHTTP encrypted modifyPassword", rpcUrl, status, modifyResponse, sendError, modifyBody);
                        return false;
                    }

                    AppendDebug(debug, "Honeywell legacy WinHTTP encrypted modifyPassword", rpcUrl, status, modifyResponse, null, modifyBody);
                    if (IsRpcSuccess(status, modifyResponse))
                    {
                        TryWinHttpDockUserPasswordSync(request, rpcUrl, loggedInSessionPart, username, newPassword, oldPassword, publicKey, cipherHint, debug);
                        return true;
                    }

                    string modifyPlainBody =
                        "{\"method\":\"Security.modifyPasswordPlain\"," +
                        "\"params\":{\"salt\":\"" + JsonEscape(payload.Salt) + "\"," +
                        "\"cipher\":\"AES-128\"," +
                        "\"content\":\"" + JsonEscape(payload.Content) + "\"}," +
                        "\"id\":7" + loggedInSessionPart + "}";

                    string modifyPlainResponse;
                    if (SendWinHttpRequest(request, rpcUrl, "POST", modifyPlainBody, out modifyPlainResponse, out status, out sendError))
                    {
                        AppendDebug(debug, "Honeywell legacy WinHTTP encrypted modifyPasswordPlain", rpcUrl, status, modifyPlainResponse, null, modifyPlainBody);
                        if (IsRpcSuccess(status, modifyPlainResponse))
                            return true;
                    }
                    else
                    {
                        AppendDebug(debug, "Honeywell legacy WinHTTP encrypted modifyPasswordPlain", rpcUrl, status, modifyPlainResponse, sendError, modifyPlainBody);
                    }

                    string loginTo = "Login to " + serial;
                    string newHash = Md5Hex(username + ":" + loginTo + ":" + newPassword).ToUpperInvariant();
                    string oldHash = Md5Hex(username + ":" + loginTo + ":" + oldPassword).ToUpperInvariant();
                    string userManagerBody =
                        "{\"method\":\"userManager.modifyPassword\"," +
                        "\"params\":{\"name\":\"" + JsonEscape(username) + "\"," +
                        "\"pwd\":\"" + JsonEscape(newHash) + "\"," +
                        "\"pwdOld\":\"" + JsonEscape(oldHash) + "\"}," +
                        "\"id\":8" + loggedInSessionPart + "}";

                    string userManagerResponse;
                    if (SendWinHttpRequest(request, rpcUrl, "POST", userManagerBody, out userManagerResponse, out status, out sendError))
                    {
                        AppendDebug(debug, "Honeywell legacy WinHTTP userManager.modifyPassword", rpcUrl, status, userManagerResponse, null, userManagerBody);
                        if (IsRpcSuccess(status, userManagerResponse))
                            return true;
                    }
                    else
                    {
                        AppendDebug(debug, "Honeywell legacy WinHTTP userManager.modifyPassword", rpcUrl, status, userManagerResponse, sendError, userManagerBody);
                    }

                    if (TryWinHttpDevInitAccount(request, rpcUrl, loggedInSessionPart, username, newPassword, debug))
                        return true;

                    if (!IsRpcSuccess(status, modifyResponse))
                    {
                        error = "WinHTTP password change rejected. modifyPasswordByType: " + DescribeRpcFailure(HttpStatusCode.OK, modifyResponse);
                        return false;
                    }

                    return false;
                }
                finally
                {
                    if (request != null && Marshal.IsComObject(request))
                        Marshal.FinalReleaseComObject(request);
                }
            }
            catch (Exception ex)
            {
                error = "WinHTTP legacy flow failed: " + ex.GetType().Name + ": " + Truncate(ex.Message, 160);
                AppendDebug(debug, "Honeywell legacy WinHTTP flow", "https://" + ipAddress + "/", 0, null, error, null);
                return false;
            }
        }

        private static bool TryWinHttpDevInitAccount(
            object request,
            string rpcUrl,
            string loggedInSessionPart,
            string username,
            string newPassword,
            StringBuilder debug)
        {
            string response;
            HttpStatusCode status;
            string sendError;

            string getPubBody = "{\"method\":\"PasswdFind.getPub\",\"params\":null,\"id\":9" + loggedInSessionPart + "}";
            if (!SendWinHttpRequest(request, rpcUrl, "POST", getPubBody, out response, out status, out sendError))
            {
                AppendDebug(debug, "Honeywell legacy WinHTTP PasswdFind.getPub", rpcUrl, status, response, sendError, getPubBody);
                return false;
            }

            AppendDebug(debug, "Honeywell legacy WinHTTP PasswdFind.getPub", rpcUrl, status, response, null, getPubBody);
            if (!IsRpcSuccess(status, response))
                return false;

            string publicKey = ExtractJsonString(response, "pub");
            if (string.IsNullOrWhiteSpace(publicKey))
                return false;

            string accountJson =
                "{\"name\":\"" + JsonEscape(username) + "\"," +
                "\"pwd\":\"" + JsonEscape(newPassword) + "\"}";

            EncryptedPayload payload;
            try
            {
                payload = EncryptPayload(publicKey, "AES", accountJson);
            }
            catch (Exception ex)
            {
                AppendDebug(debug, "Honeywell legacy WinHTTP DevInit.account encrypt", rpcUrl, 0, null, ex.GetType().Name + ": " + ex.Message, accountJson);
                return false;
            }

            string body =
                "{\"method\":\"DevInit.account\"," +
                "\"params\":{\"salt\":\"" + JsonEscape(payload.Salt) + "\"," +
                "\"cipher\":\"AES-128\"," +
                "\"content\":\"" + JsonEscape(payload.Content) + "\"}," +
                "\"id\":10" + loggedInSessionPart + "}";

            if (!SendWinHttpRequest(request, rpcUrl, "POST", body, out response, out status, out sendError))
            {
                AppendDebug(debug, "Honeywell legacy WinHTTP DevInit.account", rpcUrl, status, response, sendError, body);
                return false;
            }

            AppendDebug(debug, "Honeywell legacy WinHTTP DevInit.account", rpcUrl, status, response, null, body);
            return IsRpcSuccess(status, response);
        }

        private static void TryWinHttpDockUserPasswordSync(
            object request,
            string rpcUrl,
            string loggedInSessionPart,
            string username,
            string newPassword,
            string oldPassword,
            string publicKey,
            string cipherHint,
            StringBuilder debug)
        {
            string passwordJson =
                "{\"name\":\"" + JsonEscape(username) + "\"," +
                "\"pwd\":\"" + JsonEscape(newPassword) + "\"," +
                "\"pwdOld\":\"" + JsonEscape(oldPassword) + "\"}";

            try
            {
                EncryptedPayload payload = EncryptPayload(publicKey, cipherHint, passwordJson);
                string body =
                    "{\"method\":\"DockUser.modifyPassword\"," +
                    "\"params\":{\"salt\":\"" + JsonEscape(payload.Salt) + "\"," +
                    "\"cipher\":\"AES-128\"," +
                    "\"content\":\"" + JsonEscape(payload.Content) + "\"}," +
                    "\"id\":7" + loggedInSessionPart + "}";

                string response;
                HttpStatusCode status;
                string sendError;
                if (SendWinHttpRequest(request, rpcUrl, "POST", body, out response, out status, out sendError))
                    AppendDebug(debug, "Honeywell legacy WinHTTP DockUser.modifyPassword", rpcUrl, status, response, null, body);
                else
                    AppendDebug(debug, "Honeywell legacy WinHTTP DockUser.modifyPassword", rpcUrl, status, response, sendError, body);
            }
            catch (Exception ex)
            {
                AppendDebug(debug, "Honeywell legacy WinHTTP DockUser.modifyPassword encrypt", rpcUrl, 0, null, ex.GetType().Name + ": " + ex.Message, passwordJson);
            }
        }

        private static bool TryDefaultPasswordChange(
            string ipAddress,
            string username,
            string newPassword,
            CookieContainer cookies,
            StringBuilder debug,
            out string error)
        {
            error = "";

            string oldPassword = "1234";
            string loginUrl = "https://" + ipAddress + "/RPC2_Login";
            string rpcUrl = "https://" + ipAddress + "/RPC2";

            string firstBody =
                "{\"method\":\"global.login\"," +
                "\"params\":{\"userName\":\"" + JsonEscape(username) + "\",\"password\":\"\",\"clientType\":\"Web3.0\",\"loginType\":\"Direct\"}," +
                "\"id\":1}";

            string firstResponse;
            HttpStatusCode status;
            string sendError;
            if (!SendJson(loginUrl, firstBody, cookies, out firstResponse, out status, out sendError))
            {
                error = "Default-login first step failed: " + sendError;
                AppendDebug(debug, "Honeywell legacy default firstLogin", loginUrl, status, firstResponse, sendError, firstBody);
                return false;
            }

            AppendDebug(debug, "Honeywell legacy default firstLogin", loginUrl, status, firstResponse, null, firstBody);

            string session = ExtractJsonString(firstResponse, "session") ?? ExtractJsonNumber(firstResponse, "session");
            string encryption = ExtractJsonString(firstResponse, "encryption");
            string random = ExtractJsonString(firstResponse, "random");
            string realm = ExtractJsonString(firstResponse, "realm");

            string authPassword = BuildLoginAuth(username, oldPassword, encryption, random, realm);
            string secondSessionPart = BuildSessionPart(session);
            string secondBody =
                "{\"method\":\"global.login\"," +
                "\"params\":{\"userName\":\"" + JsonEscape(username) + "\"," +
                "\"password\":\"" + JsonEscape(authPassword) + "\"," +
                "\"clientType\":\"Web3.0\"," +
                "\"loginType\":\"Direct\"," +
                "\"authorityType\":\"" + JsonEscape(encryption) + "\"}," +
                "\"id\":2" + secondSessionPart + "}";

            string serialFromRealm = null;
            if (!string.IsNullOrWhiteSpace(realm) &&
                realm.StartsWith("Login to ", StringComparison.OrdinalIgnoreCase))
            {
                serialFromRealm = realm.Substring("Login to ".Length);
            }

            string secondResponse;
            if (!SendJson(loginUrl, secondBody, cookies, out secondResponse, out status, out sendError))
            {
                error = "Default-login second step failed: " + sendError;
                AppendDebug(debug, "Honeywell legacy default secondLogin", loginUrl, status, secondResponse, sendError, secondBody);
                return false;
            }

            AppendDebug(debug, "Honeywell legacy default secondLogin", loginUrl, status, secondResponse, null, secondBody);
            if (!IsRpcSuccess(status, secondResponse))
            {
                error = "Default-login second step rejected: " + DescribeRpcFailure(status, secondResponse);
                string remaining = ExtractJsonNumber(secondResponse, "remainLoginTimes");
                if (!string.IsNullOrWhiteSpace(remaining))
                    error += " (remaining login tries: " + remaining + ")";
                return false;
            }

            session = ExtractJsonString(secondResponse, "session") ?? ExtractJsonNumber(secondResponse, "session") ?? session;
            string loggedInSessionPart = BuildSessionPart(session);

            string notifyUrl = "https://" + ipAddress + "/RPC2_Notify_Method";
            string notifyResponse;
            if (SendGet(notifyUrl, cookies, out notifyResponse, out status, out sendError))
                AppendDebug(debug, "Honeywell legacy notify method", notifyUrl, status, notifyResponse, null, null);
            else
                AppendDebug(debug, "Honeywell legacy notify method", notifyUrl, status, notifyResponse, sendError, null);

            string warmupBody = "{\"method\":\"system.listMethod\",\"params\":null,\"id\":3" + loggedInSessionPart + "}";
            string warmupResponse;
            if (SendJson(rpcUrl, warmupBody, cookies, out warmupResponse, out status, out sendError))
                AppendDebug(debug, "Honeywell legacy logged-in system.listMethod", rpcUrl, status, warmupResponse, null, warmupBody);
            else
                AppendDebug(debug, "Honeywell legacy logged-in system.listMethod", rpcUrl, status, warmupResponse, sendError, warmupBody);

            string serial = serialFromRealm;

            if (string.IsNullOrWhiteSpace(serial))
            {
                string serialBody = "{\"method\":\"magicBox.getSerialNo\",\"params\":null,\"id\":4" + loggedInSessionPart + "}";
                string serialResponse;
                if (!SendJson(rpcUrl, serialBody, cookies, out serialResponse, out status, out sendError))
                {
                    AppendDebug(debug, "Honeywell legacy serial", rpcUrl, status, serialResponse, sendError, serialBody);
                    serialResponse = "";
                }
                else
                {
                    AppendDebug(debug, "Honeywell legacy serial", rpcUrl, status, serialResponse, null, serialBody);
                    if (!IsRpcSuccess(status, serialResponse))
                        AppendDebug(debug, "Honeywell legacy serial fallback", rpcUrl, status, "Serial lookup failed.", null, null);
                }

                serial = ExtractJsonString(serialResponse, "sn") ??
                         ExtractJsonString(serialResponse, "serial") ??
                         ExtractJsonString(serialResponse, "result");
            }

            if (string.IsNullOrWhiteSpace(serial))
            {
                error = "Serial lookup did not return serial number.";
                return false;
            }

            string loginTo = "Login to " + serial;
            string newHash = Md5Hex(username + ":" + loginTo + ":" + newPassword).ToUpperInvariant();
            string oldHash = Md5Hex(username + ":" + loginTo + ":" + oldPassword).ToUpperInvariant();

            if (TryLoggedInEncryptedPasswordChange(rpcUrl, loggedInSessionPart, username, newPassword, oldPassword, cookies, debug, out error))
                return true;

            string modifyBody =
                "{\"method\":\"userManager.modifyPassword\"," +
                "\"params\":{\"name\":\"" + JsonEscape(username) + "\"," +
                "\"pwd\":\"" + newHash + "\"," +
                "\"pwdOld\":\"" + oldHash + "\"}," +
                "\"id\":7" + loggedInSessionPart + "}";

            string modifyResponse;
            if (!SendJson(rpcUrl, modifyBody, cookies, out modifyResponse, out status, out sendError))
            {
                error = "Default password change failed: " + sendError;
                AppendDebug(debug, "Honeywell legacy modifyPassword", rpcUrl, status, modifyResponse, sendError, modifyBody);
                return false;
            }

            AppendDebug(debug, "Honeywell legacy modifyPassword", rpcUrl, status, modifyResponse, null, modifyBody);
            if (IsRpcSuccess(status, modifyResponse))
                return true;

            error = "Default password change rejected: " + DescribeRpcFailure(status, modifyResponse);
            return false;
        }

        private static bool TryDefaultPasswordChangeSingleConnection(
            string ipAddress,
            string username,
            string newPassword,
            StringBuilder debug,
            out string error,
            out bool reachedLogin)
        {
            error = "";
            reachedLogin = false;

            string oldPassword = "1234";

            try
            {
                using (TcpClient client = new TcpClient())
                {
                    client.ReceiveTimeout = 8000;
                    client.SendTimeout = 8000;
                    client.Connect(ipAddress, 443);

                    using (SslStream ssl = new SslStream(client.GetStream(), false, (s, c, ch, e) => true))
                    {
                        ssl.ReadTimeout = 8000;
                        ssl.WriteTimeout = 8000;
                        ssl.AuthenticateAsClient(ipAddress);

                        string firstBody =
                            "{\"method\":\"global.login\"," +
                            "\"params\":{\"userName\":\"" + JsonEscape(username) + "\",\"password\":\"\",\"clientType\":\"Web3.0\",\"loginType\":\"Direct\"}," +
                            "\"id\":1}";

                        string firstResponse;
                        HttpStatusCode status;
                        string sendError;
                        if (!SendRawSessionRequest(ssl, ipAddress, "POST", "/RPC2_Login", firstBody, out firstResponse, out status, out sendError))
                        {
                            error = "Single-connection first login failed: " + sendError;
                            AppendDebug(debug, "Honeywell legacy single-connection firstLogin", "https://" + ipAddress + "/RPC2_Login", status, firstResponse, sendError, firstBody);
                            return false;
                        }

                        AppendDebug(debug, "Honeywell legacy single-connection firstLogin", "https://" + ipAddress + "/RPC2_Login", status, firstResponse, null, firstBody);
                        System.Threading.Thread.Sleep(150);

                        string session = ExtractJsonString(firstResponse, "session") ?? ExtractJsonNumber(firstResponse, "session");
                        string encryption = ExtractJsonString(firstResponse, "encryption");
                        string random = ExtractJsonString(firstResponse, "random");
                        string realm = ExtractJsonString(firstResponse, "realm");
                        string authPassword = BuildLoginAuth(username, oldPassword, encryption, random, realm);
                        string secondSessionPart = BuildSessionPart(session);

                        string secondBody =
                            "{\"method\":\"global.login\"," +
                            "\"params\":{\"userName\":\"" + JsonEscape(username) + "\"," +
                            "\"password\":\"" + JsonEscape(authPassword) + "\"," +
                            "\"clientType\":\"Web3.0\"," +
                            "\"loginType\":\"Direct\"," +
                            "\"authorityType\":\"" + JsonEscape(encryption) + "\"}," +
                            "\"id\":2" + secondSessionPart + "}";

                        string secondResponse;
                        if (!SendRawSessionRequest(ssl, ipAddress, "POST", "/RPC2_Login", secondBody, out secondResponse, out status, out sendError))
                        {
                            error = "Single-connection second login failed: " + sendError;
                            AppendDebug(debug, "Honeywell legacy single-connection secondLogin", "https://" + ipAddress + "/RPC2_Login", status, secondResponse, sendError, secondBody);
                            return false;
                        }

                        AppendDebug(debug, "Honeywell legacy single-connection secondLogin", "https://" + ipAddress + "/RPC2_Login", status, secondResponse, null, secondBody);
                        if (!IsRpcSuccess(status, secondResponse))
                        {
                            error = "Single-connection second login rejected: " + DescribeRpcFailure(status, secondResponse);
                            string remaining = ExtractJsonNumber(secondResponse, "remainLoginTimes");
                            if (!string.IsNullOrWhiteSpace(remaining))
                                error += " (remaining login tries: " + remaining + ")";
                            return false;
                        }

                        reachedLogin = true;
                        session = ExtractJsonString(secondResponse, "session") ?? ExtractJsonNumber(secondResponse, "session") ?? session;
                        string loggedInSessionPart = BuildSessionPart(session);

                        string notifyResponse;
                        if (SendRawSessionRequest(ssl, ipAddress, "GET", "/RPC2_Notify_Method", null, out notifyResponse, out status, out sendError))
                            AppendDebug(debug, "Honeywell legacy single-connection notify method", "https://" + ipAddress + "/RPC2_Notify_Method", status, notifyResponse, null, null);
                        else
                            AppendDebug(debug, "Honeywell legacy single-connection notify method", "https://" + ipAddress + "/RPC2_Notify_Method", status, notifyResponse, sendError, null);

                        string serial = null;
                        if (!string.IsNullOrWhiteSpace(realm) &&
                            realm.StartsWith("Login to ", StringComparison.OrdinalIgnoreCase))
                        {
                            serial = realm.Substring("Login to ".Length);
                        }

                        if (string.IsNullOrWhiteSpace(serial))
                        {
                            error = "Single-connection login realm did not include serial number.";
                            return false;
                        }

                        string loginTo = "Login to " + serial;
                        string newHash = Md5Hex(username + ":" + loginTo + ":" + newPassword).ToUpperInvariant();
                        string oldHash = Md5Hex(username + ":" + loginTo + ":" + oldPassword).ToUpperInvariant();

                        string encryptInfoBody = "{\"method\":\"Security.getEncryptInfo\",\"params\":null,\"id\":3" + loggedInSessionPart + "}";
                        string encryptInfoResponse;
                        if (SendRawSessionRequest(ssl, ipAddress, "POST", "/RPC2", encryptInfoBody, out encryptInfoResponse, out status, out sendError))
                        {
                            AppendDebug(debug, "Honeywell legacy single-connection Security.getEncryptInfo", "https://" + ipAddress + "/RPC2", status, encryptInfoResponse, null, encryptInfoBody);
                            if (IsRpcSuccess(status, encryptInfoResponse))
                            {
                                string publicKey = ExtractJsonString(encryptInfoResponse, "pub");
                                string cipherHint = ExtractJsonString(encryptInfoResponse, "cipher");
                                if (!string.IsNullOrWhiteSpace(publicKey))
                                {
                                    string passwordJson =
                                        "{\"name\":\"" + JsonEscape(username) + "\"," +
                                        "\"pwd\":\"" + JsonEscape(newHash) + "\"," +
                                        "\"pwdOld\":\"" + JsonEscape(oldHash) + "\"}";

                                    EncryptedPayload payload = EncryptPayload(publicKey, cipherHint, passwordJson);
                                    string encryptedModifyBody =
                                        "{\"method\":\"Security.modifyPasswordByType\"," +
                                        "\"params\":{\"salt\":\"" + JsonEscape(payload.Salt) + "\"," +
                                        "\"cipher\":\"AES-128\"," +
                                        "\"content\":\"" + JsonEscape(payload.Content) + "\"}," +
                                        "\"id\":4" + loggedInSessionPart + "}";

                                    string encryptedModifyResponse;
                                    if (!SendRawSessionRequest(ssl, ipAddress, "POST", "/RPC2", encryptedModifyBody, out encryptedModifyResponse, out status, out sendError))
                                    {
                                        error = "Single-connection encrypted password change failed: " + sendError;
                                        AppendDebug(debug, "Honeywell legacy single-connection encrypted modifyPassword", "https://" + ipAddress + "/RPC2", status, encryptedModifyResponse, sendError, encryptedModifyBody);
                                        return false;
                                    }

                                    AppendDebug(debug, "Honeywell legacy single-connection encrypted modifyPassword", "https://" + ipAddress + "/RPC2", status, encryptedModifyResponse, null, encryptedModifyBody);
                                    if (IsRpcSuccess(status, encryptedModifyResponse))
                                        return true;

                                    error = "Single-connection encrypted password change rejected: " + DescribeRpcFailure(status, encryptedModifyResponse);
                                    return false;
                                }
                            }
                        }
                        else
                        {
                            AppendDebug(debug, "Honeywell legacy single-connection Security.getEncryptInfo", "https://" + ipAddress + "/RPC2", status, encryptInfoResponse, sendError, encryptInfoBody);
                        }

                        string plainModifyBody =
                            "{\"method\":\"userManager.modifyPassword\"," +
                            "\"params\":{\"name\":\"" + JsonEscape(username) + "\"," +
                            "\"pwd\":\"" + newHash + "\"," +
                            "\"pwdOld\":\"" + oldHash + "\"}," +
                            "\"id\":4" + loggedInSessionPart + "}";

                        string plainModifyResponse;
                        if (!SendRawSessionRequest(ssl, ipAddress, "POST", "/RPC2", plainModifyBody, out plainModifyResponse, out status, out sendError))
                        {
                            error = "Single-connection plain password change failed: " + sendError;
                            AppendDebug(debug, "Honeywell legacy single-connection modifyPassword", "https://" + ipAddress + "/RPC2", status, plainModifyResponse, sendError, plainModifyBody);
                            return false;
                        }

                        AppendDebug(debug, "Honeywell legacy single-connection modifyPassword", "https://" + ipAddress + "/RPC2", status, plainModifyResponse, null, plainModifyBody);
                        if (IsRpcSuccess(status, plainModifyResponse))
                            return true;

                        error = "Single-connection plain password change rejected: " + DescribeRpcFailure(status, plainModifyResponse);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                error = "Single-connection legacy flow failed: " + ex.GetType().Name + ": " + Truncate(ex.Message, 160);
                AppendDebug(debug, "Honeywell legacy single-connection flow", "https://" + ipAddress + "/", 0, null, error, null);
                return false;
            }
        }

        private static bool TryPasswordChangeAfterChallengeOnPersistentConnection(
            string ipAddress,
            string username,
            string newPassword,
            string oldPassword,
            string authPassword,
            string secondLoginBody,
            string challengeSession,
            string serial,
            StringBuilder debug,
            out string error)
        {
            error = "";

            try
            {
                using (TcpClient client = new TcpClient())
                {
                    client.ReceiveTimeout = 8000;
                    client.SendTimeout = 8000;
                    client.Connect(ipAddress, 443);

                    using (SslStream ssl = new SslStream(client.GetStream(), false, (s, c, ch, e) => true))
                    {
                        ssl.ReadTimeout = 8000;
                        ssl.WriteTimeout = 8000;
                        ssl.AuthenticateAsClient(ipAddress);

                        string response;
                        HttpStatusCode status;
                        string sendError;
                        if (!SendRawSessionRequest(ssl, ipAddress, "POST", "/RPC2_Login", secondLoginBody, out response, out status, out sendError))
                        {
                            error = "Persistent second login failed: " + sendError;
                            AppendDebug(debug, "Honeywell legacy persistent secondLogin", "https://" + ipAddress + "/RPC2_Login", status, response, sendError, secondLoginBody);
                            return false;
                        }

                        AppendDebug(debug, "Honeywell legacy persistent secondLogin", "https://" + ipAddress + "/RPC2_Login", status, response, null, secondLoginBody);
                        if (!IsRpcSuccess(status, response))
                        {
                            error = "Persistent second login rejected: " + DescribeRpcFailure(status, response);
                            string remaining = ExtractJsonNumber(response, "remainLoginTimes");
                            if (!string.IsNullOrWhiteSpace(remaining))
                                error += " (remaining login tries: " + remaining + ")";
                            return false;
                        }

                        string session = ExtractJsonString(response, "session") ?? ExtractJsonNumber(response, "session") ?? challengeSession;
                        string loggedInSessionPart = BuildSessionPart(session);

                        if (SendRawSessionRequest(ssl, ipAddress, "GET", "/RPC2_Notify_Method", null, out response, out status, out sendError))
                            AppendDebug(debug, "Honeywell legacy persistent notify method", "https://" + ipAddress + "/RPC2_Notify_Method", status, response, null, null);
                        else
                            AppendDebug(debug, "Honeywell legacy persistent notify method", "https://" + ipAddress + "/RPC2_Notify_Method", status, response, sendError, null);

                        string warmupBody = "{\"method\":\"system.listMethod\",\"params\":null,\"id\":3" + loggedInSessionPart + "}";
                        if (SendRawSessionRequest(ssl, ipAddress, "POST", "/RPC2", warmupBody, out response, out status, out sendError))
                            AppendDebug(debug, "Honeywell legacy persistent system.listMethod", "https://" + ipAddress + "/RPC2", status, response, null, warmupBody);
                        else
                            AppendDebug(debug, "Honeywell legacy persistent system.listMethod", "https://" + ipAddress + "/RPC2", status, response, sendError, warmupBody);

                        if (string.IsNullOrWhiteSpace(serial))
                        {
                            error = "Persistent flow did not have serial number from login realm.";
                            return false;
                        }

                        string loginTo = "Login to " + serial;
                        string newHash = Md5Hex(username + ":" + loginTo + ":" + newPassword).ToUpperInvariant();
                        string oldHash = Md5Hex(username + ":" + loginTo + ":" + oldPassword).ToUpperInvariant();

                        string encryptInfoBody = "{\"method\":\"Security.getEncryptInfo\",\"params\":null,\"id\":5" + loggedInSessionPart + "}";
                        if (SendRawSessionRequest(ssl, ipAddress, "POST", "/RPC2", encryptInfoBody, out response, out status, out sendError))
                        {
                            AppendDebug(debug, "Honeywell legacy persistent Security.getEncryptInfo", "https://" + ipAddress + "/RPC2", status, response, null, encryptInfoBody);
                            if (IsRpcSuccess(status, response))
                            {
                                string publicKey = ExtractJsonString(response, "pub");
                                string cipherHint = ExtractJsonString(response, "cipher");
                                if (!string.IsNullOrWhiteSpace(publicKey))
                                {
                                    string passwordJson =
                                        "{\"name\":\"" + JsonEscape(username) + "\"," +
                                        "\"pwd\":\"" + JsonEscape(newHash) + "\"," +
                                        "\"pwdOld\":\"" + JsonEscape(oldHash) + "\"}";

                                    EncryptedPayload payload = EncryptPayload(publicKey, cipherHint, passwordJson);
                                    string encryptedModifyBody =
                                        "{\"method\":\"Security.modifyPasswordByType\"," +
                                        "\"params\":{\"salt\":\"" + JsonEscape(payload.Salt) + "\"," +
                                        "\"cipher\":\"AES-128\"," +
                                        "\"content\":\"" + JsonEscape(payload.Content) + "\"}," +
                                        "\"id\":6" + loggedInSessionPart + "}";

                                    if (!SendRawSessionRequest(ssl, ipAddress, "POST", "/RPC2", encryptedModifyBody, out response, out status, out sendError))
                                    {
                                        error = "Persistent encrypted password change failed: " + sendError;
                                        AppendDebug(debug, "Honeywell legacy persistent encrypted modifyPassword", "https://" + ipAddress + "/RPC2", status, response, sendError, encryptedModifyBody);
                                        return false;
                                    }

                                    AppendDebug(debug, "Honeywell legacy persistent encrypted modifyPassword", "https://" + ipAddress + "/RPC2", status, response, null, encryptedModifyBody);
                                    if (IsRpcSuccess(status, response))
                                        return true;

                                    error = "Persistent encrypted password change rejected: " + DescribeRpcFailure(status, response);
                                    return false;
                                }
                            }
                        }
                        else
                        {
                            AppendDebug(debug, "Honeywell legacy persistent Security.getEncryptInfo", "https://" + ipAddress + "/RPC2", status, response, sendError, encryptInfoBody);
                        }

                        string plainModifyBody =
                            "{\"method\":\"userManager.modifyPassword\"," +
                            "\"params\":{\"name\":\"" + JsonEscape(username) + "\"," +
                            "\"pwd\":\"" + newHash + "\"," +
                            "\"pwdOld\":\"" + oldHash + "\"}," +
                            "\"id\":7" + loggedInSessionPart + "}";

                        if (!SendRawSessionRequest(ssl, ipAddress, "POST", "/RPC2", plainModifyBody, out response, out status, out sendError))
                        {
                            error = "Persistent plain password change failed: " + sendError;
                            AppendDebug(debug, "Honeywell legacy persistent modifyPassword", "https://" + ipAddress + "/RPC2", status, response, sendError, plainModifyBody);
                            return false;
                        }

                        AppendDebug(debug, "Honeywell legacy persistent modifyPassword", "https://" + ipAddress + "/RPC2", status, response, null, plainModifyBody);
                        if (IsRpcSuccess(status, response))
                            return true;

                        error = "Persistent plain password change rejected: " + DescribeRpcFailure(status, response);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                error = "Persistent legacy flow failed: " + ex.GetType().Name + ": " + Truncate(ex.Message, 160);
                AppendDebug(debug, "Honeywell legacy persistent flow", "https://" + ipAddress + "/", 0, null, error, null);
                return false;
            }
        }

        private static bool TryLoggedInEncryptedPasswordChange(
            string rpcUrl,
            string loggedInSessionPart,
            string username,
            string newPassword,
            string oldPassword,
            CookieContainer cookies,
            StringBuilder debug,
            out string error)
        {
            error = "";

            string encryptInfoBody = "{\"method\":\"Security.getEncryptInfo\",\"params\":null,\"id\":5" + loggedInSessionPart + "}";
            string encryptInfoResponse;
            HttpStatusCode status;
            string sendError;
            if (!SendJson(rpcUrl, encryptInfoBody, cookies, out encryptInfoResponse, out status, out sendError))
            {
                error = "Logged-in Security.getEncryptInfo failed: " + sendError;
                AppendDebug(debug, "Honeywell legacy logged-in Security.getEncryptInfo", rpcUrl, status, encryptInfoResponse, sendError, encryptInfoBody);
                return false;
            }

            AppendDebug(debug, "Honeywell legacy logged-in Security.getEncryptInfo", rpcUrl, status, encryptInfoResponse, null, encryptInfoBody);
            if (!IsRpcSuccess(status, encryptInfoResponse))
            {
                error = "Logged-in Security.getEncryptInfo rejected: " + DescribeRpcFailure(status, encryptInfoResponse);
                return false;
            }

            string publicKey = ExtractJsonString(encryptInfoResponse, "pub");
            string cipherHint = ExtractJsonString(encryptInfoResponse, "cipher");
            if (string.IsNullOrWhiteSpace(publicKey))
            {
                error = "Logged-in Security.getEncryptInfo did not return a public key.";
                return false;
            }

            string passwordJson =
                "{\"name\":\"" + JsonEscape(username) + "\"," +
                "\"pwd\":\"" + JsonEscape(newPassword) + "\"," +
                "\"pwdOld\":\"" + JsonEscape(oldPassword) + "\"}";

            EncryptedPayload payload;
            try
            {
                payload = EncryptPayload(publicKey, cipherHint, passwordJson);
            }
            catch (Exception ex)
            {
                error = "Encrypted password-change payload failed: " + ex.GetType().Name + ": " + ex.Message;
                AppendDebug(debug, "Honeywell legacy encrypted modifyPassword encrypt", rpcUrl, 0, null, error, passwordJson);
                return false;
            }

            string modifyBody =
                "{\"method\":\"Security.modifyPasswordByType\"," +
                "\"params\":{\"salt\":\"" + JsonEscape(payload.Salt) + "\"," +
                "\"cipher\":\"AES-128\"," +
                "\"content\":\"" + JsonEscape(payload.Content) + "\"}," +
                "\"id\":6" + loggedInSessionPart + "}";

            string modifyResponse;
            if (!SendJson(rpcUrl, modifyBody, cookies, out modifyResponse, out status, out sendError))
            {
                error = "Encrypted password change failed: " + sendError;
                AppendDebug(debug, "Honeywell legacy encrypted modifyPassword", rpcUrl, status, modifyResponse, sendError, modifyBody);
                return false;
            }

            AppendDebug(debug, "Honeywell legacy encrypted modifyPassword", rpcUrl, status, modifyResponse, null, modifyBody);
            if (IsRpcSuccess(status, modifyResponse))
            {
                TryLoggedInDockUserPasswordSync(rpcUrl, loggedInSessionPart, username, newPassword, oldPassword, publicKey, cipherHint, cookies, debug);
                return true;
            }

            error = "Encrypted password change rejected: " + DescribeRpcFailure(status, modifyResponse);
            return false;
        }

        private static void TryLoggedInDockUserPasswordSync(
            string rpcUrl,
            string loggedInSessionPart,
            string username,
            string newPassword,
            string oldPassword,
            string publicKey,
            string cipherHint,
            CookieContainer cookies,
            StringBuilder debug)
        {
            string passwordJson =
                "{\"name\":\"" + JsonEscape(username) + "\"," +
                "\"pwd\":\"" + JsonEscape(newPassword) + "\"," +
                "\"pwdOld\":\"" + JsonEscape(oldPassword) + "\"}";

            try
            {
                EncryptedPayload payload = EncryptPayload(publicKey, cipherHint, passwordJson);
                string body =
                    "{\"method\":\"DockUser.modifyPassword\"," +
                    "\"params\":{\"salt\":\"" + JsonEscape(payload.Salt) + "\"," +
                    "\"cipher\":\"AES-128\"," +
                    "\"content\":\"" + JsonEscape(payload.Content) + "\"}," +
                    "\"id\":7" + loggedInSessionPart + "}";

                string response;
                HttpStatusCode status;
                string sendError;
                if (SendJson(rpcUrl, body, cookies, out response, out status, out sendError))
                    AppendDebug(debug, "Honeywell legacy DockUser.modifyPassword", rpcUrl, status, response, null, body);
                else
                    AppendDebug(debug, "Honeywell legacy DockUser.modifyPassword", rpcUrl, status, response, sendError, body);
            }
            catch (Exception ex)
            {
                AppendDebug(debug, "Honeywell legacy DockUser.modifyPassword encrypt", rpcUrl, 0, null, ex.GetType().Name + ": " + ex.Message, passwordJson);
            }
        }

        private static bool TryActivateEndpoint(
            string endpoint,
            string ipAddress,
            string username,
            string password,
            CookieContainer cookies,
            StringBuilder debug,
            out string error)
        {
            error = "";

            string statusBody = "{\"method\":\"DevInit.getStatus\",\"params\":null,\"id\":1}";
            string statusResponse;
            HttpStatusCode statusCode;
            string sendError;
            if (SendText(endpoint, statusBody, cookies, out statusResponse, out statusCode, out sendError))
                AppendDebug(debug, "Honeywell legacy DevInit.getStatus", endpoint, statusCode, statusResponse, null, statusBody);
            else
                AppendDebug(debug, "Honeywell legacy DevInit.getStatus", endpoint, statusCode, statusResponse, sendError, statusBody);

            string encryptInfoBody = "{\"method\":\"Security.getEncryptInfo\",\"params\":null,\"id\":2}";
            string encryptInfoResponse;
            if (!SendText(endpoint, encryptInfoBody, cookies, out encryptInfoResponse, out statusCode, out sendError))
            {
                error = "Security.getEncryptInfo failed: " + sendError;
                AppendDebug(debug, "Honeywell legacy Security.getEncryptInfo", endpoint, statusCode, encryptInfoResponse, sendError, encryptInfoBody);
                return false;
            }

            AppendDebug(debug, "Honeywell legacy Security.getEncryptInfo", endpoint, statusCode, encryptInfoResponse, null, encryptInfoBody);
            if (!IsRpcSuccess(statusCode, encryptInfoResponse))
            {
                error = "Security.getEncryptInfo rejected: " + DescribeRpcFailure(statusCode, encryptInfoResponse);
                return false;
            }

            string publicKey = ExtractJsonString(encryptInfoResponse, "pub");
            string cipherHint = ExtractJsonString(encryptInfoResponse, "cipher");
            if (string.IsNullOrWhiteSpace(publicKey))
            {
                error = "Security.getEncryptInfo did not return a public key.";
                return false;
            }

            string accountJson = "{\"name\":\"" + JsonEscape(username) + "\",\"pwd\":\"" + JsonEscape(password) + "\"}";
            if (!TryEncryptedCommand(endpoint, "DevInit.account", publicKey, cipherHint, accountJson, cookies, debug, out error))
                return false;

            foreach (string country in new[] { "IL", "PS", "US" })
            {
                string accessJson =
                    "{\"Country\":\"" + country + "\"," +
                    "\"Language\":\"English\"," +
                    "\"VideoStandard\":\"PAL\"," +
                    "\"NetAccess\":0," +
                    "\"UpgradeCheck\":0}";

                string accessError;
                if (TryEncryptedCommand(endpoint, "DevInit.access", publicKey, cipherHint, accessJson, cookies, debug, out accessError))
                    return true;

                error = accessError;
            }

            return false;
        }

        private static bool TryEncryptedCommand(
            string endpoint,
            string method,
            string publicKey,
            string cipherHint,
            string plainJson,
            CookieContainer cookies,
            StringBuilder debug,
            out string error)
        {
            error = "";

            EncryptedPayload payload;
            try
            {
                payload = EncryptPayload(publicKey, cipherHint, plainJson);
            }
            catch (Exception ex)
            {
                error = method + " encryption failed: " + ex.GetType().Name + ": " + ex.Message;
                AppendDebug(debug, "Honeywell legacy " + method + " encrypt", endpoint, 0, null, error, plainJson);
                return false;
            }

            string body =
                "{\"method\":\"" + method + "\"," +
                "\"params\":{\"salt\":\"" + JsonEscape(payload.Salt) + "\"," +
                "\"cipher\":\"" + JsonEscape(payload.Cipher) + "\"," +
                "\"content\":\"" + JsonEscape(payload.Content) + "\"}," +
                "\"id\":3}";

            string response;
            HttpStatusCode status;
            string sendError;
            if (!SendText(endpoint, body, cookies, out response, out status, out sendError))
            {
                error = method + " failed: " + sendError;
                AppendDebug(debug, "Honeywell legacy " + method, endpoint, status, response, sendError, body);
                return false;
            }

            AppendDebug(debug, "Honeywell legacy " + method, endpoint, status, response, null, body);
            if (IsRpcSuccess(status, response))
                return true;

            error = method + " rejected: " + DescribeRpcFailure(status, response);
            return false;
        }

        private sealed class EncryptedPayload
        {
            public string Salt;
            public string Cipher;
            public string Content;
        }

        private static EncryptedPayload EncryptPayload(string publicKey, string cipherHint, string plainJson)
        {
            string cipher = SelectCipher(cipherHint);
            int keyLength = cipher.EndsWith("-256", StringComparison.OrdinalIgnoreCase) ? 32 : 16;
            string key = CreateAsciiKey(keyLength);
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);

            byte[] encryptedKey = RsaEncrypt(publicKey, keyBytes);
            byte[] encryptedContent = AesEncrypt(Encoding.UTF8.GetBytes(plainJson), keyBytes, cipher);

            return new EncryptedPayload
            {
                Salt = BytesToHex(encryptedKey).ToLowerInvariant(),
                Cipher = cipher,
                Content = Convert.ToBase64String(encryptedContent)
            };
        }

        private static string SelectCipher(string cipherHint)
        {
            if (!string.IsNullOrWhiteSpace(cipherHint))
            {
                if (cipherHint.IndexOf("ROAE", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "ROAE-256";
                if (cipherHint.IndexOf("RPAC", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "RPAC-256";
            }

            return "AES-128";
        }

        private static string CreateAsciiKey(int length)
        {
            const string alphabet = "0123456789";
            byte[] random = new byte[length];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
                rng.GetBytes(random);

            char[] chars = new char[length];
            for (int i = 0; i < chars.Length; i++)
                chars[i] = alphabet[random[i] % alphabet.Length];

            return new string(chars);
        }

        private static byte[] RsaEncrypt(string publicKey, byte[] value)
        {
            string modulusHex = ExtractPublicKeyPart(publicKey, "N");
            string exponentHex = ExtractPublicKeyPart(publicKey, "E");
            if (string.IsNullOrWhiteSpace(modulusHex) || string.IsNullOrWhiteSpace(exponentHex))
                throw new InvalidOperationException("Unsupported Honeywell legacy public key format.");

            RSAParameters parameters = new RSAParameters
            {
                Modulus = HexToBytes(modulusHex),
                Exponent = HexToBytes(exponentHex)
            };

            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
            {
                rsa.ImportParameters(parameters);
                return rsa.Encrypt(value, false);
            }
        }

        private static byte[] AesEncrypt(byte[] value, byte[] key, string cipher)
        {
            using (AesManaged aes = new AesManaged())
            {
                aes.Key = key;
                aes.Mode = cipher.Equals("RPAC-256", StringComparison.OrdinalIgnoreCase) ? CipherMode.CBC : CipherMode.ECB;
                aes.Padding = PaddingMode.Zeros;
                if (aes.Mode == CipherMode.CBC)
                    aes.IV = Encoding.UTF8.GetBytes("0000000000000000");

                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                    return encryptor.TransformFinalBlock(value, 0, value.Length);
            }
        }

        private static void PreloadRoot(string endpoint, CookieContainer cookies, StringBuilder debug)
        {
            try
            {
                Uri endpointUri = new Uri(endpoint);
                string root = endpointUri.Scheme + "://" + endpointUri.Host + (endpointUri.IsDefaultPort ? "" : ":" + endpointUri.Port.ToString()) + "/";

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(root);
                request.Method = "GET";
                request.Timeout = 6000;
                request.ReadWriteTimeout = 6000;
                request.AllowAutoRedirect = false;
                request.KeepAlive = false;
                request.UserAgent = "Mozilla/5.0";
                request.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
                request.CookieContainer = cookies;
                request.ServicePoint.Expect100Continue = false;
                ApplyLegacyConnectionAffinity(request);

                using (HttpWebResponse response = GetResponseIncludingErrors(request))
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    string body = reader.ReadToEnd();
                    AppendDebug(debug, "Honeywell legacy preload root", root, response.StatusCode, body, null, null);
                }
            }
            catch (Exception ex)
            {
                AppendDebug(debug, "Honeywell legacy preload root", endpoint, 0, null, ex.GetType().Name + ": " + ex.Message, null);
            }
        }

        private static bool SendText(
            string url,
            string body,
            CookieContainer cookies,
            out string responseBody,
            out HttpStatusCode statusCode,
            out string error)
        {
            responseBody = "";
            statusCode = 0;
            error = "";

            for (int attempt = 0; attempt < 2; attempt++)
            {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.Timeout = 6000;
                request.ReadWriteTimeout = 6000;
                request.AllowAutoRedirect = false;
                request.KeepAlive = true;
                request.UserAgent = GetBrowserUserAgent();
                request.Accept = "application/json, text/javascript, */*; q=0.01";
                request.ContentType = LegacyAjaxContentType;
                request.CookieContainer = cookies;
                request.ServicePoint.Expect100Continue = false;
                ApplyLegacyConnectionAffinity(request);
                request.Headers["Accept-Language"] = "en-US,en;q=0.9";
                request.Headers["Sec-Fetch-Dest"] = "empty";
                request.Headers["Sec-Fetch-Mode"] = "cors";
                request.Headers["Sec-Fetch-Site"] = "same-origin";
                AddChromeClientHintHeaders(request);

                Uri uri = new Uri(url);
                string origin = uri.Scheme + "://" + uri.Host + (uri.IsDefaultPort ? "" : ":" + uri.Port.ToString());
                request.Referer = origin + "/";
                request.Headers["Origin"] = origin;
                request.Headers["X-Requested-With"] = "XMLHttpRequest";

                byte[] bytes = Encoding.UTF8.GetBytes(body);
                request.ContentLength = bytes.Length;
                using (Stream stream = request.GetRequestStream())
                    stream.Write(bytes, 0, bytes.Length);

                using (HttpWebResponse response = GetResponseIncludingErrors(request))
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    statusCode = response.StatusCode;
                    responseBody = reader.ReadToEnd();
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + Truncate(ex.Message, 160);
                if (attempt == 0 && IsRetryableConnectionClose(ex))
                    continue;
            }
            }

            string browserError = error;
            if (TryDahuaStyleRequest(url, "POST", body, "application/json", cookies, out responseBody, out statusCode, out error))
                return true;

            string dahuaStyleError = error;
            if (TryRawPost(url, body, cookies, out responseBody, out statusCode, out error))
                return true;

            error = CombineSendErrors(browserError, dahuaStyleError, error);

            return false;
        }

        private static bool SendJson(
            string url,
            string body,
            CookieContainer cookies,
            out string responseBody,
            out HttpStatusCode statusCode,
            out string error)
        {
            responseBody = "";
            statusCode = 0;
            error = "";

            for (int attempt = 0; attempt < 2; attempt++)
            {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.Timeout = 6000;
                request.ReadWriteTimeout = 6000;
                request.AllowAutoRedirect = false;
                request.KeepAlive = true;
                request.UserAgent = GetBrowserUserAgent();
                request.Accept = "application/json, text/javascript, */*; q=0.01";
                request.ContentType = LegacyAjaxContentType;
                request.CookieContainer = cookies;
                request.ServicePoint.Expect100Continue = false;
                ApplyLegacyConnectionAffinity(request);
                request.Headers["Accept-Language"] = "en-US,en;q=0.9";
                request.Headers["Sec-Fetch-Dest"] = "empty";
                request.Headers["Sec-Fetch-Mode"] = "cors";
                request.Headers["Sec-Fetch-Site"] = "same-origin";
                AddChromeClientHintHeaders(request);

                Uri uri = new Uri(url);
                string origin = uri.Scheme + "://" + uri.Host + (uri.IsDefaultPort ? "" : ":" + uri.Port.ToString());
                request.Referer = origin + "/";
                request.Headers["Origin"] = origin;
                request.Headers["X-Requested-With"] = "XMLHttpRequest";

                byte[] bytes = Encoding.UTF8.GetBytes(body);
                request.ContentLength = bytes.Length;
                using (Stream stream = request.GetRequestStream())
                    stream.Write(bytes, 0, bytes.Length);

                using (HttpWebResponse response = GetResponseIncludingErrors(request))
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    statusCode = response.StatusCode;
                    responseBody = reader.ReadToEnd();
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + Truncate(ex.Message, 160);
                if (attempt == 0 && IsRetryableConnectionClose(ex))
                    continue;
            }
            }

            string browserError = error;
            if (TryDahuaStyleRequest(url, "POST", body, "application/json", cookies, out responseBody, out statusCode, out error))
                return true;

            string dahuaStyleError = error;
            if (TryRawPost(url, body, cookies, out responseBody, out statusCode, out error))
                return true;

            error = CombineSendErrors(browserError, dahuaStyleError, error);

            return false;
        }

        private static bool SendGet(
            string url,
            CookieContainer cookies,
            out string responseBody,
            out HttpStatusCode statusCode,
            out string error)
        {
            responseBody = "";
            statusCode = 0;
            error = "";

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 6000;
                request.ReadWriteTimeout = 6000;
                request.AllowAutoRedirect = false;
                request.KeepAlive = true;
                request.UserAgent = GetBrowserUserAgent();
                request.Accept = "application/json, text/javascript, */*; q=0.01";
                request.CookieContainer = cookies;
                request.ServicePoint.Expect100Continue = false;
                ApplyLegacyConnectionAffinity(request);
                request.Headers["Accept-Language"] = "en-US,en;q=0.9";
                request.Headers["X-Requested-With"] = "XMLHttpRequest";
                request.Headers["Sec-Fetch-Dest"] = "empty";
                request.Headers["Sec-Fetch-Mode"] = "cors";
                request.Headers["Sec-Fetch-Site"] = "same-origin";
                AddChromeClientHintHeaders(request);

                Uri uri = new Uri(url);
                string origin = uri.Scheme + "://" + uri.Host + (uri.IsDefaultPort ? "" : ":" + uri.Port.ToString());
                request.Referer = origin + "/";

                using (HttpWebResponse response = GetResponseIncludingErrors(request))
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    statusCode = response.StatusCode;
                    responseBody = reader.ReadToEnd();
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + Truncate(ex.Message, 160);
            }

            string browserError = error;
            if (TryDahuaStyleRequest(url, "GET", null, null, cookies, out responseBody, out statusCode, out error))
                return true;

            string dahuaStyleError = error;
            if (TryRawRequest("GET", url, null, cookies, out responseBody, out statusCode, out error))
                return true;

            error = CombineSendErrors(browserError, dahuaStyleError, error);

            return false;
        }

        private static string CombineSendErrors(string browserError, string dahuaStyleError, string rawError)
        {
            return "Browser-style: " + (string.IsNullOrWhiteSpace(browserError) ? "no details" : browserError) +
                   " | Dahua-style: " + (string.IsNullOrWhiteSpace(dahuaStyleError) ? "no details" : dahuaStyleError) +
                   " | Raw: " + (string.IsNullOrWhiteSpace(rawError) ? "no details" : rawError);
        }

        private static bool SendRawSessionRequest(
            SslStream ssl,
            string host,
            string method,
            string path,
            string body,
            out string responseBody,
            out HttpStatusCode statusCode,
            out string error)
        {
            responseBody = "";
            statusCode = 0;
            error = "";

            try
            {
                byte[] bodyBytes = Encoding.UTF8.GetBytes(body ?? "");
                string origin = "https://" + host;
                string headers =
                    method + " " + path + " HTTP/1.1\r\n" +
                    "Host: " + host + "\r\n" +
                    "Connection: keep-alive\r\n" +
                    "User-Agent: " + GetBrowserUserAgent() + "\r\n" +
                    "Accept: application/json, text/javascript, */*; q=0.01\r\n" +
                    "Accept-Encoding: gzip, deflate\r\n" +
                    "Accept-Language: en-US,en;q=0.9,ar;q=0.8\r\n" +
                    "Referer: " + origin + "/\r\n" +
                    "Sec-Fetch-Dest: empty\r\n" +
                    "Sec-Fetch-Mode: cors\r\n" +
                    "Sec-Fetch-Site: same-origin\r\n" +
                    "X-Requested-With: XMLHttpRequest\r\n" +
                    "sec-ch-ua: \"Google Chrome\";v=\"149\", \"Chromium\";v=\"149\", \"Not)A;Brand\";v=\"24\"\r\n" +
                    "sec-ch-ua-mobile: ?0\r\n" +
                    "sec-ch-ua-platform: \"Windows\"\r\n" +
                    (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                        ? "Content-Type: " + LegacyAjaxContentType + "\r\n" +
                          "Origin: " + origin + "\r\n" +
                          "Content-Length: " + bodyBytes.Length.ToString() + "\r\n"
                        : "") +
                    "\r\n";

                byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
                ssl.Write(headerBytes, 0, headerBytes.Length);
                if (bodyBytes.Length > 0)
                    ssl.Write(bodyBytes, 0, bodyBytes.Length);
                ssl.Flush();

                string responseHeaders = ReadHttpHeaders(ssl);
                if (string.IsNullOrWhiteSpace(responseHeaders))
                {
                    error = "No HTTP headers returned on persistent TLS connection.";
                    return false;
                }

                Match statusMatch = Regex.Match(responseHeaders, @"HTTP/\d(?:\.\d)?\s+(\d+)");
                if (statusMatch.Success)
                    statusCode = (HttpStatusCode)int.Parse(statusMatch.Groups[1].Value);

                int contentLength = 0;
                Match lengthMatch = Regex.Match(responseHeaders, @"(?im)^Content-Length:\s*(\d+)\s*$");
                if (lengthMatch.Success)
                    int.TryParse(lengthMatch.Groups[1].Value, out contentLength);

                byte[] responseBytes = ReadExactBytes(ssl, contentLength);
                responseBody = Encoding.UTF8.GetString(responseBytes);

                return (int)statusCode >= 200 && (int)statusCode < 600;
            }
            catch (Exception ex)
            {
                error = "Persistent raw " + method + " " + ex.GetType().Name + ": " + Truncate(ex.Message, 160);
                return false;
            }
        }

        private static string ReadHttpHeaders(Stream stream)
        {
            MemoryStream ms = new MemoryStream();
            byte[] buffer = new byte[1];
            int matched = 0;
            byte[] terminator = { 13, 10, 13, 10 };

            while (ms.Length < 65536)
            {
                int read = stream.Read(buffer, 0, 1);
                if (read <= 0)
                    break;

                byte b = buffer[0];
                ms.WriteByte(b);
                if (b == terminator[matched])
                {
                    matched++;
                    if (matched == terminator.Length)
                        break;
                }
                else
                {
                    matched = b == terminator[0] ? 1 : 0;
                }
            }

            return Encoding.ASCII.GetString(ms.ToArray());
        }

        private static byte[] ReadExactBytes(Stream stream, int length)
        {
            if (length <= 0)
                return new byte[0];

            byte[] result = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = stream.Read(result, offset, length - offset);
                if (read <= 0)
                    break;

                offset += read;
            }

            if (offset == length)
                return result;

            byte[] partial = new byte[offset];
            Array.Copy(result, partial, offset);
            return partial;
        }

        private static bool TryDahuaStyleRequest(
            string url,
            string method,
            string body,
            string contentType,
            CookieContainer cookies,
            out string responseBody,
            out HttpStatusCode statusCode,
            out string error)
        {
            responseBody = "";
            statusCode = 0;
            error = "";

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = method;
                request.Timeout = 6000;
                request.ReadWriteTimeout = 6000;
                request.AllowAutoRedirect = false;
                request.KeepAlive = false;
                request.UserAgent = "Mozilla/5.0";
                request.Accept = "application/json, text/plain, */*";
                request.CookieContainer = cookies;
                request.ServicePoint.Expect100Continue = false;

                if (body != null)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(body);
                    request.ContentType = contentType ?? "application/json";
                    request.ContentLength = bytes.Length;
                    using (Stream stream = request.GetRequestStream())
                        stream.Write(bytes, 0, bytes.Length);
                }

                using (HttpWebResponse response = GetResponseIncludingErrors(request))
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    statusCode = response.StatusCode;
                    responseBody = reader.ReadToEnd();
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "Dahua-style " + method + " " + ex.GetType().Name + ": " + Truncate(ex.Message, 160);
                return false;
            }
        }

        private static bool TryRawPost(
            string url,
            string body,
            CookieContainer cookies,
            out string responseBody,
            out HttpStatusCode statusCode,
            out string error)
        {
            return TryRawRequest("POST", url, body, cookies, out responseBody, out statusCode, out error);
        }

        private static bool TryRawRequest(
            string method,
            string url,
            string body,
            CookieContainer cookies,
            out string responseBody,
            out HttpStatusCode statusCode,
            out string error)
        {
            responseBody = "";
            statusCode = 0;
            error = "";

            try
            {
                Uri uri = new Uri(url);
                if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                    return false;

                byte[] bodyBytes = Encoding.UTF8.GetBytes(body ?? "");
                string path = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
                string origin = uri.Scheme + "://" + uri.Host + (uri.IsDefaultPort ? "" : ":" + uri.Port.ToString());
                string cookieHeader = BuildCookieHeader(cookies, uri);
                string headers =
                    method + " " + path + " HTTP/1.1\r\n" +
                    "Host: " + uri.Host + (uri.IsDefaultPort ? "" : ":" + uri.Port.ToString()) + "\r\n" +
                    "Connection: keep-alive\r\n" +
                    "User-Agent: " + GetBrowserUserAgent() + "\r\n" +
                    "Accept: application/json, text/javascript, */*; q=0.01\r\n" +
                    "Accept-Encoding: gzip, deflate\r\n" +
                    "Accept-Language: en-US,en;q=0.9,ar;q=0.8\r\n" +
                    "Referer: " + origin + "/\r\n" +
                    "Sec-Fetch-Dest: empty\r\n" +
                    "Sec-Fetch-Mode: cors\r\n" +
                    "Sec-Fetch-Site: same-origin\r\n" +
                    "X-Requested-With: XMLHttpRequest\r\n" +
                    "sec-ch-ua: \"Google Chrome\";v=\"149\", \"Chromium\";v=\"149\", \"Not)A;Brand\";v=\"24\"\r\n" +
                    "sec-ch-ua-mobile: ?0\r\n" +
                    "sec-ch-ua-platform: \"Windows\"\r\n" +
                    (string.IsNullOrWhiteSpace(cookieHeader) ? "" : "Cookie: " + cookieHeader + "\r\n") +
                    (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                        ? "Content-Type: " + LegacyAjaxContentType + "\r\n" +
                          "Origin: " + origin + "\r\n" +
                          "Content-Length: " + bodyBytes.Length.ToString() + "\r\n"
                        : "") +
                    "\r\n";

                using (TcpClient client = new TcpClient())
                {
                    client.ReceiveTimeout = 8000;
                    client.SendTimeout = 8000;
                    client.Connect(uri.Host, uri.Port > 0 ? uri.Port : 443);

                    using (SslStream ssl = new SslStream(client.GetStream(), false, (s, c, ch, e) => true))
                    {
                        ssl.ReadTimeout = 8000;
                        ssl.WriteTimeout = 8000;
                        ssl.AuthenticateAsClient(uri.Host);

                        byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
                        ssl.Write(headerBytes, 0, headerBytes.Length);
                        if (bodyBytes.Length > 0)
                            ssl.Write(bodyBytes, 0, bodyBytes.Length);
                        ssl.Flush();

                        string responseHeaders = ReadHttpHeaders(ssl);
                        if (string.IsNullOrWhiteSpace(responseHeaders))
                        {
                            error = "Raw " + method + " returned no HTTP status. Bytes=0";
                            return false;
                        }

                        Match statusMatch = Regex.Match(responseHeaders, @"HTTP/\d(?:\.\d)?\s+(\d+)");
                        if (statusMatch.Success)
                            statusCode = (HttpStatusCode)int.Parse(statusMatch.Groups[1].Value);

                        int contentLength = 0;
                        Match lengthMatch = Regex.Match(responseHeaders, @"(?im)^Content-Length:\s*(\d+)\s*$");
                        if (lengthMatch.Success)
                            int.TryParse(lengthMatch.Groups[1].Value, out contentLength);

                        byte[] responseBytes = ReadExactBytes(ssl, contentLength);
                        responseBody = Encoding.UTF8.GetString(responseBytes);

                        if ((int)statusCode >= 200 && !string.IsNullOrEmpty(responseHeaders))
                            return true;

                        error = "Raw " + method + " returned no HTTP status. Headers=" + Truncate(responseHeaders, 120);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                error = "Raw " + method + " " + ex.GetType().Name + ": " + Truncate(ex.Message, 160);
                return false;
            }
        }

        private static void AddChromeClientHintHeaders(HttpWebRequest request)
        {
            request.Headers["sec-ch-ua"] = "\"Google Chrome\";v=\"149\", \"Chromium\";v=\"149\", \"Not)A;Brand\";v=\"24\"";
            request.Headers["sec-ch-ua-mobile"] = "?0";
            request.Headers["sec-ch-ua-platform"] = "\"Windows\"";
        }

        private static bool SendWinHttpRequest(
            object request,
            string url,
            string method,
            string body,
            out string responseBody,
            out HttpStatusCode statusCode,
            out string error)
        {
            responseBody = "";
            statusCode = 0;
            error = "";

            try
            {
                SetComProperty(request, "Option", 4, 13056);

                InvokeCom(request, "Open", method, url, false);
                InvokeCom(request, "SetTimeouts", 6000, 6000, 6000, 6000);

                InvokeCom(request, "SetRequestHeader", "User-Agent", GetBrowserUserAgent());
                InvokeCom(request, "SetRequestHeader", "Accept", "application/json, text/javascript, */*; q=0.01");
                InvokeCom(request, "SetRequestHeader", "Accept-Language", "en-US,en;q=0.9,ar;q=0.8");
                InvokeCom(request, "SetRequestHeader", "X-Requested-With", "XMLHttpRequest");
                InvokeCom(request, "SetRequestHeader", "Referer", "https://" + new Uri(url).Host + "/");
                InvokeCom(request, "SetRequestHeader", "Sec-Fetch-Dest", "empty");
                InvokeCom(request, "SetRequestHeader", "Sec-Fetch-Mode", "cors");
                InvokeCom(request, "SetRequestHeader", "Sec-Fetch-Site", "same-origin");
                InvokeCom(request, "SetRequestHeader", "sec-ch-ua", "\"Google Chrome\";v=\"149\", \"Chromium\";v=\"149\", \"Not)A;Brand\";v=\"24\"");
                InvokeCom(request, "SetRequestHeader", "sec-ch-ua-mobile", "?0");
                InvokeCom(request, "SetRequestHeader", "sec-ch-ua-platform", "\"Windows\"");

                if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    Uri uri = new Uri(url);
                    string origin = uri.Scheme + "://" + uri.Host + (uri.IsDefaultPort ? "" : ":" + uri.Port.ToString());
                    InvokeCom(request, "SetRequestHeader", "Content-Type", LegacyAjaxContentType);
                    InvokeCom(request, "SetRequestHeader", "Origin", origin);
                    InvokeCom(request, "Send", body ?? "");
                }
                else
                {
                    InvokeCom(request, "Send");
                }

                object status = GetComProperty(request, "Status");
                if (status != null)
                    statusCode = (HttpStatusCode)Convert.ToInt32(status);

                object text = GetComProperty(request, "ResponseText");
                responseBody = text == null ? "" : Convert.ToString(text);
                return (int)statusCode >= 200;
            }
            catch (Exception ex)
            {
                Exception actual = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                error = "WinHTTP " + actual.GetType().Name + ": " + Truncate(actual.Message, 220);
                return false;
            }
        }

        private static object InvokeCom(object target, string member, params object[] args)
        {
            return target.GetType().InvokeMember(
                member,
                BindingFlags.InvokeMethod,
                null,
                target,
                args);
        }

        private static object GetComProperty(object target, string property)
        {
            return target.GetType().InvokeMember(
                property,
                BindingFlags.GetProperty,
                null,
                target,
                null);
        }

        private static void SetComProperty(object target, string property, params object[] args)
        {
            target.GetType().InvokeMember(
                property,
                BindingFlags.PutDispProperty,
                null,
                target,
                args);
        }

        private static string BuildCookieHeader(CookieContainer cookies, Uri uri)
        {
            if (cookies == null || uri == null)
                return "";

            CookieCollection collection = cookies.GetCookies(uri);
            if (collection == null || collection.Count == 0)
                return "";

            StringBuilder sb = new StringBuilder();
            foreach (Cookie cookie in collection)
            {
                if (sb.Length > 0)
                    sb.Append("; ");

                sb.Append(cookie.Name);
                sb.Append("=");
                sb.Append(cookie.Value);
            }

            return sb.ToString();
        }

        private static HttpWebResponse GetResponseIncludingErrors(HttpWebRequest request)
        {
            try
            {
                return (HttpWebResponse)request.GetResponse();
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse)
            {
                return (HttpWebResponse)ex.Response;
            }
        }

        private static void ApplyLegacyConnectionAffinity(HttpWebRequest request)
        {
            if (request == null || request.RequestUri == null)
                return;

            request.ConnectionGroupName = "HoneywellLegacy-" + request.RequestUri.Host;
            request.ServicePoint.ConnectionLimit = 1;
            request.ServicePoint.MaxIdleTime = 120000;
            request.ServicePoint.Expect100Continue = false;
            request.Pipelined = false;
        }

        private static bool IsRetryableConnectionClose(Exception ex)
        {
            if (ex == null)
                return false;

            string message = ex.Message ?? "";
            return message.IndexOf("connection was closed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("unexpectedly", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("keep alive", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsConnectionClosedError(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                return false;

            return error.IndexOf("connection was closed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("closed unexpectedly", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("expected to be kept alive", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("socket hang up", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("ECONNRESET", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetBrowserUserAgent()
        {
            return "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36";
        }

        private static bool IsRpcSuccess(HttpStatusCode status, string response)
        {
            if ((int)status < 200 || (int)status >= 300)
                return false;

            string compact = Compact(response);
            return compact.IndexOf("\"result\":true", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   compact.IndexOf("\"code\":0", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   compact.IndexOf("\"error\":null", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string DescribeRpcFailure(HttpStatusCode status, string response)
        {
            string message = ExtractJsonString(response, "message") ?? ExtractJsonNumber(response, "code");
            if (!string.IsNullOrWhiteSpace(message))
                return "HTTP " + (int)status + " / " + message;

            if (!string.IsNullOrWhiteSpace(response))
                return "HTTP " + (int)status + " / " + Truncate(response, 180);

            return "HTTP " + (int)status;
        }

        private static string ExtractPublicKeyPart(string publicKey, string name)
        {
            Match m = Regex.Match(publicKey ?? "", "(^|,)" + Regex.Escape(name) + ":([0-9a-fA-F]+)");
            return m.Success ? m.Groups[2].Value : null;
        }

        private static string ExtractJsonString(string json, string field)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(field))
                return null;

            Match m = Regex.Match(json, "\"" + Regex.Escape(field) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\") : null;
        }

        private static string ExtractJsonNumber(string json, string field)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(field))
                return null;

            Match m = Regex.Match(json, "\"" + Regex.Escape(field) + "\"\\s*:\\s*(-?[0-9]+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string BuildLoginAuth(string username, string password, string encryption, string random, string realm)
        {
            if (string.Equals(encryption, "Basic", StringComparison.OrdinalIgnoreCase))
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));

            if (string.Equals(encryption, "Default", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(random) &&
                !string.IsNullOrWhiteSpace(realm))
            {
                string inner = Md5Hex(username + ":" + realm + ":" + password).ToUpperInvariant();
                return Md5Hex(username + ":" + random + ":" + inner).ToUpperInvariant();
            }

            return password;
        }

        private static string BuildSessionPart(string session)
        {
            if (string.IsNullOrWhiteSpace(session))
                return "";

            if (Regex.IsMatch(session, "^[0-9]+$"))
                return ",\"session\":" + session;

            return ",\"session\":\"" + JsonEscape(session) + "\"";
        }

        private static bool IsInvalidPasswordOrLockRisk(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                return false;

            return error.IndexOf("password not valid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("remainLoginTimes", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("remaining login tries", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("locked", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Md5Hex(string value)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                    sb.Append(b.ToString("x2"));

                return sb.ToString();
            }
        }

        private static byte[] HexToBytes(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return new byte[0];

            hex = hex.Trim();
            if ((hex.Length % 2) != 0)
                hex = "0" + hex;

            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

            return bytes;
        }

        private static string BytesToHex(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
                sb.Append(b.ToString("x2"));

            return sb.ToString();
        }

        private static void AppendDebug(
            StringBuilder debug,
            string label,
            string url,
            HttpStatusCode status,
            string response,
            string error,
            string requestBody)
        {
            if (debug == null)
                return;

            debug.Append(label).Append(" ").Append(url).Append(" -> ");

            if ((int)status != 0)
                debug.Append("HTTP ").Append((int)status);
            else
                debug.Append("no HTTP status");

            if (!string.IsNullOrWhiteSpace(error))
                debug.Append(", error=").Append(CompactLine(error, 700));

            if (!string.IsNullOrWhiteSpace(response))
                debug.Append(", body=").Append(CompactLine(response, 900));

            if (!string.IsNullOrWhiteSpace(requestBody))
                debug.Append(", request=").Append(CompactLine(ScrubPassword(requestBody), 500));

            debug.AppendLine();
        }

        private static string WriteDebugFile(string ipAddress, string result, string trace)
        {
            return "";
        }

        private static string SafeFilePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            return Regex.Replace(value.Trim(), "[^A-Za-z0-9_.-]", "_");
        }

        private static string FormatDebugPath(string path)
        {
            return "";
        }

        private static string ScrubPassword(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            string scrubbed = Regex.Replace(value, "(\"password\"\\s*:\\s*\")([^\"]*)\"", "$1***\"", RegexOptions.IgnoreCase);
            scrubbed = Regex.Replace(scrubbed, "(\"pwdOld\"\\s*:\\s*\")([^\"]*)\"", "$1***\"", RegexOptions.IgnoreCase);
            scrubbed = Regex.Replace(scrubbed, "(\"pwd\"\\s*:\\s*\")([^\"]*)\"", "$1***\"", RegexOptions.IgnoreCase);
            scrubbed = Regex.Replace(scrubbed, "(\"content\"\\s*:\\s*\")([^\"]*)\"", "$1***\"", RegexOptions.IgnoreCase);
            scrubbed = Regex.Replace(scrubbed, "(\"salt\"\\s*:\\s*\")([^\"]*)\"", "$1***\"", RegexOptions.IgnoreCase);
            return scrubbed;
        }

        private static string Compact(string text)
        {
            return (text ?? "").Replace(" ", "").Replace("\t", "").Replace("\r", "").Replace("\n", "");
        }

        private static string CompactLine(string value, int max)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            string compact = value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            while (compact.IndexOf("  ", StringComparison.Ordinal) >= 0)
                compact = compact.Replace("  ", " ");

            return Truncate(compact.Trim(), max);
        }

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value ?? "";

            return value.Substring(0, maxLength) + "...";
        }
    }
}
