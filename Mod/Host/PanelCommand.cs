using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace airimayor.Host
{
    /// <summary>
    /// One sandboxed panel command: snapshot, click, fill, press.
    /// It drives flat Gameface panels through the inspector port. It does
    /// not open that port, draw on the city, or run a shell.
    /// </summary>
    internal static class PanelCommand
    {
        internal const string LaunchFlagRequired =
            "The launch flag -uiDeveloperMode is required. Panel control listens on 127.0.0.1:9444, and this switch cannot open that port.";

        private const string Inspector = "http://127.0.0.1:9444";
        private static readonly Regex s_Ref = new Regex("^e[1-9][0-9]{0,3}$", RegexOptions.CultureInvariant);
        private static readonly SemaphoreSlim s_Gate = new SemaphoreSlim(1, 1);
        private static int s_Generation;

        internal static string ProbePort()
        {
            return PortOpen() ? "Panel control can see the game." : LaunchFlagRequired;
        }

        internal static async Task<PanelResult> RunAsync(string argumentsJson, CancellationToken cancellationToken)
        {
            string action;
            string reference;
            string text;
            string key;
            try
            {
                JObject args = string.IsNullOrWhiteSpace(argumentsJson) ? new JObject() : JObject.Parse(argumentsJson);
                action = ((string)args["action"] ?? "").Trim().ToLowerInvariant();
                reference = ((string)args["ref"] ?? "").Trim();
                text = (string)args["text"] ?? "";
                key = ((string)args["key"] ?? "").Trim();
            }
            catch (Exception)
            {
                return PanelResult.Reject("Provide action snapshot, click, fill, or press.");
            }

            if (action != "snapshot" && action != "click" && action != "fill" && action != "press")
            {
                return PanelResult.Reject("action must be snapshot, click, fill, or press.");
            }
            if (!PortOpen())
            {
                return PanelResult.Reject(LaunchFlagRequired);
            }

            await s_Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using (InspectorSession session = await InspectorSession.OpenAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (action == "snapshot")
                    {
                        return await SnapshotAsync(session, cancellationToken).ConfigureAwait(false);
                    }
                    if (action == "click")
                    {
                        return await ClickAsync(session, reference, cancellationToken).ConfigureAwait(false);
                    }
                    if (action == "fill")
                    {
                        return await FillAsync(session, reference, text, cancellationToken).ConfigureAwait(false);
                    }
                    return await PressAsync(session, key, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                if (!PortOpen() || string.Equals(e.Message, LaunchFlagRequired, StringComparison.Ordinal))
                {
                    return PanelResult.Reject(LaunchFlagRequired);
                }
                return PanelResult.Reject("Panel control could not read the screen. " + e.Message);
            }
            finally
            {
                s_Gate.Release();
            }
        }

        private static async Task<PanelResult> SnapshotAsync(InspectorSession session, CancellationToken cancellationToken)
        {
            s_Generation = s_Generation >= 100000 ? 1 : s_Generation + 1;
            JObject payload = await session.EvaluateAsync(SnapshotScript(s_Generation), cancellationToken).ConfigureAwait(false);
            if (payload["ok"] == null || !(bool)payload["ok"])
            {
                return PanelResult.Reject((string)payload["error"] ?? "Snapshot failed.");
            }
            string text = (string)payload["text"] ?? "";
            return PanelResult.Succeed(text.Length == 0 ? "No controls on screen." : text);
        }

        private static async Task<PanelResult> ClickAsync(InspectorSession session, string reference, CancellationToken cancellationToken)
        {
            if (!s_Ref.IsMatch(reference ?? ""))
            {
                return PanelResult.Reject("click needs a number from the latest snapshot, such as e1.");
            }
            JObject payload = await session.EvaluateAsync(
                ClickScript(s_Generation, reference),
                cancellationToken).ConfigureAwait(false);
            return FromPayload(payload);
        }

        private static async Task<PanelResult> FillAsync(
            InspectorSession session,
            string reference,
            string text,
            CancellationToken cancellationToken)
        {
            if (!s_Ref.IsMatch(reference ?? ""))
            {
                return PanelResult.Reject("fill needs a number from the latest snapshot, such as e1.");
            }
            JObject payload = await session.EvaluateAsync(
                FillScript(s_Generation, reference, text ?? ""),
                cancellationToken).ConfigureAwait(false);
            return FromPayload(payload);
        }

        private static async Task<PanelResult> PressAsync(InspectorSession session, string key, CancellationToken cancellationToken)
        {
            if (!KeyCodes.TryGetValue(key ?? "", out int code))
            {
                return PanelResult.Reject("press needs one key, such as Escape, Enter, Tab, or a letter.");
            }
            string name = KeyCodes.Name(key);
            await session.KeyAsync(name, code, cancellationToken).ConfigureAwait(false);
            return PanelResult.Succeed("Pressed " + name + ".");
        }

        private static PanelResult FromPayload(JObject payload)
        {
            if (payload["ok"] != null && (bool)payload["ok"])
            {
                return PanelResult.Succeed((string)payload["text"] ?? "Done.");
            }
            return PanelResult.Reject((string)payload["error"] ?? "That control is gone. Take a snapshot again.");
        }

        private static bool PortOpen()
        {
            var client = new TcpClient();
            try
            {
                IAsyncResult result = client.BeginConnect("127.0.0.1", 9444, null, null);
                if (!result.AsyncWaitHandle.WaitOne(400))
                {
                    return false;
                }
                client.EndConnect(result);
                return client.Connected;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                client.Close();
            }
        }

        private static string SnapshotScript(int generation)
        {
            return "(function(){" +
                "var previous=window.__airimayorPanelRefs||[];" +
                "for(var i=0;i<previous.length;i++){if(previous[i]&&previous[i].removeAttribute)previous[i].removeAttribute('data-airimayor-ref');}" +
                "window.__airimayorPanelGen=" + generation + ";" +
                "window.__airimayorPanelRefs=[];" +
                "function inChat(el){var node=el;while(node){if(node.getAttribute&&node.getAttribute('data-airimayor-chat')!=null)return true;node=node.parentElement;}return false;}" +
                "var nodes=document.querySelectorAll('button, input, textarea, select, a');" +
                "var lines=[];var count=0;" +
                "for(var n=0;n<nodes.length;n++){" +
                "var el=nodes[n];if(inChat(el))continue;" +
                "var box=el.getBoundingClientRect();if(!box||box.width<1||box.height<1)continue;" +
                "count++;if(count>80)break;" +
                "var ref='e'+count;el.setAttribute('data-airimayor-ref',ref);window.__airimayorPanelRefs.push(el);" +
                "var kind=el.tagName==='INPUT'&&el.type==='range'?'slider':(el.tagName==='INPUT'||el.tagName==='TEXTAREA'?'textbox':(el.tagName==='A'?'link':'button'));" +
                "var label=(el.getAttribute('aria-label')||el.getAttribute('placeholder')||el.value||el.textContent||'').replace(/\\s+/g,' ').trim();" +
                "if(label.length>80)label=label.substring(0,80);" +
                "lines.push(ref+' '+kind+' '+label);" +
                "}" +
                "var text=lines.join('\\n');" +
                "if(text.length>6000)text=text.substring(0,6000)+'\\n…';" +
                "return JSON.stringify({ok:true,text:text});" +
                "})()";
        }

        private static string ClickScript(int generation, string reference)
        {
            return "(function(){" +
                "if(window.__airimayorPanelGen!==" + generation + ")return JSON.stringify({ok:false,error:'That number expired. Take a snapshot again.'});" +
                "var el=null;var refs=window.__airimayorPanelRefs||[];" +
                "for(var i=0;i<refs.length;i++){if(refs[i]&&refs[i].getAttribute&&refs[i].getAttribute('data-airimayor-ref')===" + JsonConvert.SerializeObject(reference) + ")el=refs[i];}" +
                "if(!el)return JSON.stringify({ok:false,error:'That number expired. Take a snapshot again.'});" +
                "var node=el;while(node){if(node.getAttribute&&node.getAttribute('data-airimayor-chat')!=null)return JSON.stringify({ok:false,error:'That control is the mayor chat.'});node=node.parentElement;}" +
                "el.dispatchEvent(new MouseEvent('mousedown',{bubbles:true,cancelable:true,view:window}));" +
                "el.dispatchEvent(new MouseEvent('mouseup',{bubbles:true,cancelable:true,view:window}));" +
                "el.dispatchEvent(new MouseEvent('click',{bubbles:true,cancelable:true,view:window}));" +
                "var label=(el.getAttribute('aria-label')||el.textContent||'').replace(/\\s+/g,' ').trim();" +
                "return JSON.stringify({ok:true,text:'Pressed '+(label||" + JsonConvert.SerializeObject(reference) + ")+'.'});" +
                "})()";
        }

        private static string FillScript(int generation, string reference, string text)
        {
            return "(function(){" +
                "if(window.__airimayorPanelGen!==" + generation + ")return JSON.stringify({ok:false,error:'That number expired. Take a snapshot again.'});" +
                "var el=null;var refs=window.__airimayorPanelRefs||[];" +
                "for(var i=0;i<refs.length;i++){if(refs[i]&&refs[i].getAttribute&&refs[i].getAttribute('data-airimayor-ref')===" + JsonConvert.SerializeObject(reference) + ")el=refs[i];}" +
                "if(!el)return JSON.stringify({ok:false,error:'That number expired. Take a snapshot again.'});" +
                "var node=el;while(node){if(node.getAttribute&&node.getAttribute('data-airimayor-chat')!=null)return JSON.stringify({ok:false,error:'That control is the mayor chat.'});node=node.parentElement;}" +
                "var tag=el.tagName;if(tag!=='INPUT'&&tag!=='TEXTAREA')return JSON.stringify({ok:false,error:'That control is not a text box or slider.'});" +
                "var proto=tag==='TEXTAREA'?window.HTMLTextAreaElement.prototype:window.HTMLInputElement.prototype;" +
                "var desc=Object.getOwnPropertyDescriptor(proto,'value');" +
                "if(desc&&desc.set)desc.set.call(el," + JsonConvert.SerializeObject(text) + ");else el.value=" + JsonConvert.SerializeObject(text) + ";" +
                "el.dispatchEvent(new Event('input',{bubbles:true}));" +
                "el.dispatchEvent(new Event('change',{bubbles:true}));" +
                "return JSON.stringify({ok:true,text:'Wrote '+String(el.value).substring(0,80)});"+
                "})()";
        }

        private static class KeyCodes
        {
            private static readonly Dictionary<string, int> s_Codes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Escape"] = 27,
                ["Enter"] = 13,
                ["Tab"] = 9,
                ["Space"] = 32,
                ["Backspace"] = 8,
                ["ArrowUp"] = 38,
                ["ArrowDown"] = 40,
                ["ArrowLeft"] = 37,
                ["ArrowRight"] = 39,
            };

            public static bool TryGetValue(string key, out int code)
            {
                if (s_Codes.TryGetValue(key, out code))
                {
                    return true;
                }
                if (key.Length == 1 && key[0] >= ' ' && key[0] < 127)
                {
                    code = char.ToUpperInvariant(key[0]);
                    return true;
                }
                code = 0;
                return false;
            }

            public static string Name(string key)
            {
                if (key.Length == 1)
                {
                    return key.ToUpperInvariant();
                }
                if (string.Equals(key, "Space", StringComparison.OrdinalIgnoreCase))
                {
                    return "Space";
                }
                foreach (string name in s_Codes.Keys)
                {
                    if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                    {
                        return name;
                    }
                }
                return key;
            }
        }

        private sealed class InspectorSession : IDisposable
        {
            private readonly ClientWebSocket m_Socket = new ClientWebSocket();
            private int m_NextId;

            public static async Task<InspectorSession> OpenAsync(CancellationToken cancellationToken)
            {
                string page;
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) })
                {
                    string body = await http.GetStringAsync(Inspector + "/json/list").ConfigureAwait(false);
                    JArray pages = JArray.Parse(body);
                    page = pages.Count == 0 ? null : (string)pages[0]["webSocketDebuggerUrl"];
                }
                if (string.IsNullOrEmpty(page))
                {
                    throw new InvalidOperationException(LaunchFlagRequired);
                }
                var session = new InspectorSession();
                await session.m_Socket.ConnectAsync(new Uri(page), cancellationToken).ConfigureAwait(false);
                return session;
            }

            public async Task<JObject> EvaluateAsync(string expression, CancellationToken cancellationToken)
            {
                JObject message = await CallAsync("Runtime.evaluate", new JObject
                {
                    ["expression"] = expression,
                    ["returnByValue"] = true,
                }, cancellationToken).ConfigureAwait(false);
                JToken exception = message["result"]?["exceptionDetails"];
                if (exception != null)
                {
                    string text = (string)exception["text"] ?? (string)exception["exception"]?["description"] ?? "The screen rejected the command.";
                    return new JObject { ["ok"] = false, ["error"] = text };
                }
                JToken value = message["result"]?["result"]?["value"];
                if (value == null)
                {
                    return new JObject { ["ok"] = false, ["error"] = "The screen returned nothing." };
                }
                if (value.Type == JTokenType.String)
                {
                    return JObject.Parse((string)value);
                }
                return value as JObject ?? new JObject { ["ok"] = false, ["error"] = "The screen returned nothing." };
            }

            public async Task KeyAsync(string key, int code, CancellationToken cancellationToken)
            {
                var down = new JObject
                {
                    ["type"] = "keyDown",
                    ["key"] = key,
                    ["code"] = key,
                    ["windowsVirtualKeyCode"] = code,
                    ["nativeVirtualKeyCode"] = code,
                };
                await CallAsync("Input.dispatchKeyEvent", down, cancellationToken).ConfigureAwait(false);
                down["type"] = "keyUp";
                await CallAsync("Input.dispatchKeyEvent", down, cancellationToken).ConfigureAwait(false);
            }

            public void Dispose()
            {
                try
                {
                    m_Socket.Dispose();
                }
                catch (Exception)
                {
                }
            }

            private async Task<JObject> CallAsync(string method, JObject parameters, CancellationToken cancellationToken)
            {
                int id = ++m_NextId;
                var request = new JObject
                {
                    ["id"] = id,
                    ["method"] = method,
                    ["params"] = parameters,
                };
                byte[] bytes = Encoding.UTF8.GetBytes(request.ToString(Formatting.None));
                await m_Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
                var buffer = new byte[8192];
                while (true)
                {
                    var message = new StringBuilder();
                    WebSocketReceiveResult received;
                    do
                    {
                        received = await m_Socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                        if (received.MessageType == WebSocketMessageType.Close)
                        {
                            throw new InvalidOperationException("The inspector closed.");
                        }
                        message.Append(Encoding.UTF8.GetString(buffer, 0, received.Count));
                    }
                    while (!received.EndOfMessage);
                    JObject parsed = JObject.Parse(message.ToString());
                    JToken responseId = parsed["id"];
                    if (responseId != null && responseId.Type == JTokenType.Integer && (int)responseId == id)
                    {
                        return parsed;
                    }
                }
            }
        }
    }

    internal struct PanelResult
    {
        public bool Ok;
        public string Text;

        public static PanelResult Succeed(string text)
        {
            return new PanelResult { Ok = true, Text = text ?? "" };
        }

        public static PanelResult Reject(string text)
        {
            return new PanelResult { Ok = false, Text = text ?? "" };
        }
    }
}
