using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HttpServer.MVC;
using HttpServer.HttpModules;
using System.IO;
using Duplicati.Server.Serialization;

namespace Duplicati.Server
{
    public class WebServer
    {
        /// <summary>
        /// Option for changing the webroot folder
        /// </summary>
        private const string OPTION_WEBROOT = "webservice-webroot";
        /// <summary>
        /// Option for changing the webservice listen port
        /// </summary>
        private const string OPTION_PORT = "webservice-port";

        /// <summary>
        /// The single webserver instance
        /// </summary>
        private HttpServer.HttpServer m_server;

        /// <summary>
        /// Sets up the webserver and starts it
        /// </summary>
        /// <param name="options">A set of options</param>
        public WebServer(IDictionary<string, string> options)
        {
            m_server = new HttpServer.HttpServer();

            m_server.Add(new DynamicHandler());

            string webroot = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
#if DEBUG
            //For debug we go "../../../.." to get out of "GUI/Duplicati.GUI.TrayIcon/bin/debug"
            string tmpwebroot = System.IO.Path.GetFullPath(System.IO.Path.Combine(webroot, "..", "..", "..", ".."));
            tmpwebroot = System.IO.Path.Combine(tmpwebroot, "Server");
            if (System.IO.Directory.Exists(System.IO.Path.Combine(tmpwebroot, "webroot")))
                webroot = tmpwebroot;
            else {
                //If we are running the server standalone, we only need to exit "bin/Debug"
                tmpwebroot = System.IO.Path.GetFullPath(System.IO.Path.Combine(webroot, "..", ".."));
                if (System.IO.Directory.Exists(System.IO.Path.Combine(tmpwebroot, "webroot")))
                    webroot = tmpwebroot;
            }

            if (Library.Utility.Utility.IsClientOSX) 
            {
                string osxTmpWebRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(webroot, "..", "..", "..", "..", "..", "..", ".."));
                osxTmpWebRoot = System.IO.Path.Combine(osxTmpWebRoot, "Server");
                if (System.IO.Directory.Exists(System.IO.Path.Combine(osxTmpWebRoot, "webroot")))
                    webroot = osxTmpWebRoot;
            }
#endif

            webroot = System.IO.Path.Combine(webroot, "webroot");

            if (options.ContainsKey(OPTION_WEBROOT))
            {
                string userroot = options[OPTION_WEBROOT];
#if DEBUG
                //In debug mode we do not care where the path points
#else
                //In release mode we check that the usersupplied path is located
                // in the same folders as the running application, to avoid users
                // that inadvertently expose top level folders
                if (!string.IsNullOrWhiteSpace(userroot)
                    &&
                    (
                        userroot.StartsWith(Library.Utility.Utility.AppendDirSeparator(System.Reflection.Assembly.GetExecutingAssembly().Location), Library.Utility.Utility.ClientFilenameStringComparision)
                        ||
                        userroot.StartsWith(Library.Utility.Utility.AppendDirSeparator(Program.StartupPath), Library.Utility.Utility.ClientFilenameStringComparision)
                    )
                )
#endif
                {
                    webroot = userroot;
                }
            }

            FileModule fh = new FileModule("/", webroot);
            fh.AddDefaultMimeTypes();
            fh.MimeTypes.Add("htc", "text/x-component");
            fh.MimeTypes.Add("json", "application/json");
            m_server.Add(fh);
            m_server.Add(new IndexHtmlHandler(System.IO.Path.Combine(webroot, "status-window.html")));
#if DEBUG
            //For debugging, it is nice to know when we get a 404
            m_server.Add(new DebugReportHandler());
#endif

            int port;
            string portstring;
            if (!options.TryGetValue(OPTION_PORT, out portstring) || !int.TryParse(portstring, out port))
                port = 8080;

            m_server.Start(System.Net.IPAddress.Any, port);
        }

        private class BodyWriter : System.IO.StreamWriter, IDisposable
        {
            private HttpServer.IHttpResponse m_resp;

            // We override the format provider so all JSON output uses US format
            public override IFormatProvider FormatProvider
            {
                get { return System.Globalization.CultureInfo.InvariantCulture; }
            }

            public BodyWriter(HttpServer.IHttpResponse resp)
                : base(resp.Body,  resp.Encoding)
            {
                m_resp = resp;
            }

            protected override void Dispose (bool disposing)
            {
                if (!m_resp.HeadersSent)
                {
                    base.Flush();
                    m_resp.ContentLength = base.BaseStream.Length;
                    m_resp.Send();
                }
                base.Dispose(disposing);
            }
        }

        private class IndexHtmlHandler : HttpModule
        {
            private string m_defaultdoc;

            public IndexHtmlHandler(string defaultdoc) { m_defaultdoc = defaultdoc; }

            public override bool Process(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
            {
                if ((request.Uri.AbsolutePath == "/" || request.Uri.AbsolutePath == "/index.html" || request.Uri.AbsolutePath == "/index.htm") && System.IO.File.Exists(m_defaultdoc))
                {
                    response.Status = System.Net.HttpStatusCode.OK;
                    response.Reason = "OK";
                    response.ContentType = "text/html";

                    using (var fs = System.IO.File.OpenRead(m_defaultdoc))
                    {
                        response.ContentLength = fs.Length;
                        response.Body = fs;
                        response.Send();
                    }

                    return true;
                }

                return false;
            }
        }

        private class DebugReportHandler : HttpModule
        {
            public override bool Process(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
            {
                System.Diagnostics.Trace.WriteLine(string.Format("Rejecting request for {0}", request.Uri));
                return false;
            }
        }

        private class DynamicHandler : HttpModule
        {
            private delegate void ProcessSub(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter writer);
            private readonly Dictionary<string, ProcessSub> SUPPORTED_METHODS;

            public DynamicHandler()
            {
                SUPPORTED_METHODS = new Dictionary<string, ProcessSub>(System.StringComparer.InvariantCultureIgnoreCase);
             
                //Make a list of all supported actions
                SUPPORTED_METHODS.Add("supported-actions", ListSupportedActions);
                SUPPORTED_METHODS.Add("system-info", ListSystemInfo);
                SUPPORTED_METHODS.Add("list-backups", ListBackups);
                SUPPORTED_METHODS.Add("get-current-state", GetCurrentState);
                SUPPORTED_METHODS.Add("get-progress-state", GetProgressState);
                SUPPORTED_METHODS.Add("list-application-settings", ListApplicationSettings);
                SUPPORTED_METHODS.Add("list-options", ListCoreOptions);
                SUPPORTED_METHODS.Add("send-command", SendCommand);
                SUPPORTED_METHODS.Add("get-backup-defaults", GetBackupDefaults);
                SUPPORTED_METHODS.Add("get-folder-contents", GetFolderContents);
                SUPPORTED_METHODS.Add("get-backup", GetBackup);
                SUPPORTED_METHODS.Add("add-backup", AddBackup);
                SUPPORTED_METHODS.Add("update-backup", UpdateBackup);
                SUPPORTED_METHODS.Add("delete-backup", DeleteBackup);
            }

            public override bool Process (HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session)
            {
                //We use the fake entry point /control.cgi to listen for requests
                //This ensures that the rest of the webserver can just serve plain files
                if (!request.Uri.AbsolutePath.Equals("/control.cgi", StringComparison.InvariantCultureIgnoreCase))
                    return false;

                HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;

                string action = input["action"].Value ?? "";
             
                //Lookup the actual handler method
                ProcessSub method;
                SUPPORTED_METHODS.TryGetValue(action, out method);

                if (method == null) {
                    response.Status = System.Net.HttpStatusCode.NotImplemented;
                    response.Reason = "Unsupported action: " + (action == null ? "<null>" : "");
                    response.Send();
                } else {
                    //Default setup
                    response.Status = System.Net.HttpStatusCode.OK;
                    response.Reason = "OK";
#if DEBUG
                    response.ContentType = "text/plain";
#else
                    response.ContentType = "text/json";
#endif
                    using (BodyWriter bw = new BodyWriter(response))
                    {
                        try
                        {
                            method(request, response, session, bw);
                        }
                        catch (Exception ex)
                        {
                            if (!response.HeadersSent)
                            {
                                response.Status = System.Net.HttpStatusCode.InternalServerError;
                                response.Reason = ex.Message;
                                response.ContentType = "text/plain";
                                bw.WriteLine("Internal error");
#if DEBUG
                                bw.Write("Stacktrace: " + ex.ToString());
#endif
                                bw.Flush();
                            }
                        }
                    }
                }

                return true;
            }

            private void ReportError(HttpServer.IHttpResponse response, BodyWriter bw, string message)
            {
                response.Status = System.Net.HttpStatusCode.InternalServerError;
                response.Reason = message;

                OutputObject(bw, new { Error = message });
            }
            
            private void OutputObject (BodyWriter b, object o)
            {
                Serializer.SerializeJson(b, o);
            }

            private void ListSystemInfo(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                OutputObject(bw, new
                {
                    APIVersion = 1,
                    ServerVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                    ServerVersionName = License.VersionNumbers.Version,
                    ServerTime = DateTime.Now,
                    OSType = Library.Utility.Utility.IsClientLinux ? (Library.Utility.Utility.IsClientOSX ? "OSX" : "Linux") : "Windows",
                    DirectorySeparator = System.IO.Path.DirectorySeparatorChar,
                    PathSeparator = System.IO.Path.PathSeparator,
                    CaseSensitiveFilesystem = Duplicati.Library.Utility.Utility.IsFSCaseSensitive,
                    MonoVersion = Duplicati.Library.Utility.Utility.IsMono ? Duplicati.Library.Utility.Utility.MonoVersion.ToString() : null,
                    MachineName = System.Environment.MachineName,
                    NewLine = System.Environment.NewLine,
                    CLRVersion = System.Environment.Version.ToString(),
                    CLROSInfo = new
                    {
                        Platform = System.Environment.OSVersion.Platform.ToString(),
                        ServicePack = System.Environment.OSVersion.ServicePack,
                        Version = System.Environment.OSVersion.Version.ToString(),
                        VersionString = System.Environment.OSVersion.VersionString
                    },
                    CompressionModules =  Serializable.ServerSettings.CompressionModules,
                    EncryptionModules = Serializable.ServerSettings.EncryptionModules,
                    BackendModules = Serializable.ServerSettings.BackendModules,
                    GenericModules = Serializable.ServerSettings.GenericModules
                });
            }

            private void ListSupportedActions(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                OutputObject(bw, new { Version = 1, Methods = SUPPORTED_METHODS.Keys });
            }

            private void ListBackups (HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                OutputObject(bw, Program.DataConnection.Backups);
            }

            private void GetFolderContents(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
                if(input["path"] == null || input["path"].Value == null)
                {
                    ReportError(response, bw, "The path parameter was not set");
                    return;
                }

                bool skipFiles = Library.Utility.Utility.ParseBool(input["onlyfolders"].Value, false);

                string path = input["path"].Value;
                if (!path.StartsWith("/"))
                {
                    ReportError(response, bw, "The path parameter must start with a forward-slash");
                    return;
                }

                try
                {
                    if (!path.EndsWith("/"))
                        path += '/';

                    IEnumerable<Serializable.TreeNode> res;

                    if (!Library.Utility.Utility.IsClientLinux && path.Equals("/"))
                    {
                        res = 
                            from di in System.IO.DriveInfo.GetDrives()
                            where di.DriveType == DriveType.Fixed || di.DriveType == DriveType.Network || di.DriveType == DriveType.Removable
                            select new Serializable.TreeNode()
                            {
                                id = "/" + di.RootDirectory.FullName.Replace('\\', '/'),
                                text = di.RootDirectory.FullName.Replace('\\', ' ') + "(" + di.DriveType + ")",
                                iconCls = "x-tree-icon-drive"
                            };
                    }
                    else
                    {
                        //Helper function for finding out if a folder has sub elements
                        Func<string, bool> hasSubElements = (p) => skipFiles ? Directory.EnumerateDirectories(p).Any() : Directory.EnumerateFileSystemEntries(p).Any();

                        //Helper function for dealing with exceptions when accessing off-limits folders
                        Func<string, bool> isEmptyFolder = (p) =>
                        {
                            try { return !hasSubElements(p); }
                            catch { }
                            return true;
                        };

                        //Helper function for dealing with exceptions when accessing off-limits folders
                        Func<string, bool> canAccess = (p) =>
                        {
                            try { hasSubElements(p); return true; }
                            catch { }
                            return false;
                        };

                        res = 
                            from s in System.IO.Directory.EnumerateFileSystemEntries(Library.Utility.Utility.IsClientLinux ? path : path.Substring(1).Replace('/', '\\'))
                                  
                            let attr = System.IO.File.GetAttributes(s)
                            let isSymlink = (attr & FileAttributes.ReparsePoint) != 0
                            let isFolder = (attr & FileAttributes.Directory) != 0
                            let isFile = !isFolder
                            let isHidden = (attr & FileAttributes.Hidden) != 0

                            let accesible = isFile || canAccess(s)
                            let isLeaf = isFile || !accesible || isEmptyFolder(s) 

                            let rawid = isFolder ? Library.Utility.Utility.AppendDirSeparator(s) : s

                            where !skipFiles || isFolder
                                  
                            select new Serializable.TreeNode()
                            {
                                id = Library.Utility.Utility.IsClientLinux ? rawid : "/" + rawid.Replace('\\', '/'),
                                text = System.IO.Path.GetFileName(s),
                                iconCls = isFolder ? (accesible ? "x-tree-icon-parent" : "x-tree-icon-locked") : "x-tree-icon-leaf",
                                leaf = isLeaf
                            };
                    }


                    OutputObject(bw, res);
                }
                catch (Exception ex)
                {
                    ReportError(response, bw, "Failed to process the path: " + ex.Message);
                }
            }

            private bool LongPollCheck(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, BodyWriter bw, EventPollNotify poller, ref long id, out bool isError)
            {
                HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
                if (Library.Utility.Utility.ParseBool(input["longpoll"].Value, false))
                {
                    long lastEventId;
                    if (!long.TryParse(input["lasteventid"].Value, out lastEventId))
                    {
                        ReportError(response, bw, "When activating long poll, the request must include the last event id");
                        isError = true;
                        return false;
                    }

                    TimeSpan ts;
                    try { ts = Library.Utility.Timeparser.ParseTimeSpan(input["duration"].Value); }
                    catch (Exception ex)
                    {
                        ReportError(response, bw, "Invalid duration: " + ex.Message);
                        isError = true;
                        return false;
                    }

                    if (ts <= TimeSpan.FromSeconds(10) || ts.TotalMilliseconds > int.MaxValue)
                    {
                        ReportError(response, bw, "Invalid duration, must be at least 10 seconds, and less than " + int.MaxValue + " milliseconds");
                        isError = true;
                        return false;
                    }

                    isError = false;
                    id = poller.Wait(lastEventId, (int)ts.TotalMilliseconds);
                    return true;
                }

                isError = false;
                return false;
            }

            private void GetProgressState(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                bool isError;
                long id = 0;
                if (LongPollCheck(request, response, bw, Program.ProgressEventNotifyer, ref id, out isError) || !isError)
                {
                    //TODO: Don't block if the backup is completed when entering the wait state
                    /*var ev = Program.Runner.LastEvent;
                    ev.LastEventID = id;
                    OutputObject(bw, ev);*/
                }
            }

            private void GetCurrentState (HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                bool isError;
                long id = 0;
                if (LongPollCheck(request, response, bw, Program.StatusEventNotifyer, ref id, out isError))
                {
                    //Make sure we do not report a higher number than the eventnotifyer says
                    var st = new Serializable.ServerStatus();
                    st.LastEventID = id;
                    OutputObject(bw, st);
                }
                else if (!isError)
                {
                    OutputObject(bw, new Serializable.ServerStatus());
                }
            }

            private void ListCoreOptions(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                OutputObject(bw, new Duplicati.Library.Main.Options(new Dictionary<string, string>()).SupportedCommands);
            }

            private void ListApplicationSettings(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                OutputObject(bw, Program.DataConnection.ApplicationSettings);
            }

            private void GetBackupDefaults(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {   
                //TODO: Read newbackup.json and return it             
                
                OutputObject(bw, new
                {
                    success = true,
                    data = new
                    {
                        Options = Program.DataConnection.Settings
                    }
                });
            }

            private void GetBackup(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;
                long id;
                if (!long.TryParse(input["id"].Value, out id))
                    ReportError(response, bw, "Invalid or missing backup id");
                else
                {
                    var bk = Program.DataConnection.GetBackup(id);
                    if (bk == null)
                        ReportError(response, bw, "Invalid or missing backup id");
                    else                
                        OutputObject(bw, new
                        {
                            success = true,
                            data = bk
                        });
                }
            }

            private void UpdateBackup(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                string str = request.Form["data"].Value;
                if (string.IsNullOrWhiteSpace(str))
                {
                    ReportError(response, bw, "Missing backup object");
                    return;
                }

                long id;
                if (!long.TryParse(request.Form["id"].Value, out id))
                {
                    ReportError(response, bw, "Invalid or missing backup id");
                    return;
                }

                try
                {
                    lock(Program.DataConnection.m_lock)
                    {
                        var backup = Program.DataConnection.GetBackup(id);
                        if (backup == null)
                        {
                            ReportError(response, bw, "Invalid or missing backup id");
                            return;
                        }
    
                        backup = Serializer.Deserialize<Database.Backup>(new StringReader(str));
                        backup.ID = id;
                        
                        //TODO: Validate, duplicate names etc.
                        Program.DataConnection.AddOrUpdateBackup(backup);
                    }
                    
                    OutputObject(bw, new { status = "OK" });
                }
                catch (Exception ex)
                {
                    ReportError(response, bw, string.Format("Unable to parse backup object: {0}", ex.Message));
                }
            }

            private void AddBackup(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                string str = request.Form["data"].Value;
                if (string.IsNullOrWhiteSpace(str))
                {
                    ReportError(response, bw, "Missing backup object");
                    return;
                }

                try
                {
                    lock(Program.DataConnection.m_lock)
                    {
                        var backup = Serializer.Deserialize<Database.Backup>(new StringReader(str));
                        backup.ID = -1;
    
                        //TODO: Validate, duplicate names etc.
    
                        Program.DataConnection.AddOrUpdateBackup(backup);
                    }
                    
                    OutputObject(bw, new { status = "OK" });
                }
                catch (Exception ex)
                {
                    ReportError(response, bw, string.Format("Unable to parse schedule or task object: {0}", ex.Message));
                }
            }

            private void DeleteBackup(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;

                long id;
                if (!long.TryParse(input["id"].Value, out id))
                {
                    ReportError(response, bw, "Invalid or missing backup id");
                    return;
                }


                var backup = Program.DataConnection.GetBackup(id);
                if (backup == null)
                {
                    ReportError(response, bw, "Invalid or missing backup id");
                    return;
                }

                if (Program.WorkThread.Active)
                {
                    try
                    {
                        //TODO: It's not safe to access the values like this, 
                        //because the runner thread might interfere
                        if (Program.WorkThread.CurrentTask.Item1 == id)
                        {
                            bool force;
                            if (!bool.TryParse(input["force"].Value, out force))
                                force = false;
                            
                            if (!force)
                            {
                                OutputObject(bw, new { status = "failed", reason = "backup-in-progress" });
                                return;
                            }

                            bool hasPaused = Program.LiveControl.State == LiveControls.LiveControlState.Paused;
                            Program.LiveControl.Pause();

                            try
                            {
                                for (int i = 0; i < 10; i++)
                                    if (Program.WorkThread.Active)
                                    {
                                        var t = Program.WorkThread.CurrentTask;
                                        if (t != null && t.Item1 == id)
                                            System.Threading.Thread.Sleep(1000);
                                        else
                                            break;
                                    }
                                    else
                                        break;
                            }
                            finally
                            {
                            }

                            if (Program.WorkThread.Active)
                            {
                                var t = Program.WorkThread.CurrentTask;
                                if (t == null && t.Item1 == id)
                                {
                                    if (hasPaused)
                                        Program.LiveControl.Resume();
                                    OutputObject(bw, new { status = "failed", reason = "backup-unstoppable" });
                                    return;
                                }
                            }

                            if (hasPaused)
                                Program.LiveControl.Resume();
                        }
                    }
                    catch (Exception ex)
                    {
                        OutputObject(bw, new { status = "error", message = ex.Message });
                        return;
                    }
                }

                //We have fiddled with the schedules
                Program.Scheduler.Reschedule();

                OutputObject(bw, new { status = "OK" });
            }

            private void SendCommand(HttpServer.IHttpRequest request, HttpServer.IHttpResponse response, HttpServer.Sessions.IHttpSession session, BodyWriter bw)
            {
                HttpServer.HttpInput input = request.Method.ToUpper() == "POST" ? request.Form : request.QueryString;

                string command = input["command"].Value ?? "";
                long id;

                switch (command.ToLowerInvariant())
                {
                    case "pause":
                        if (input.Contains("duration") && !string.IsNullOrWhiteSpace(input["duration"].Value))
                        {
                            TimeSpan ts;
                            try { ts = Library.Utility.Timeparser.ParseTimeSpan(input["duration"].Value); }
                            catch (Exception ex)
                            {
                                ReportError(response, bw, ex.Message);
                                return;
                            }
                            if (ts.TotalMilliseconds > 0)
                                Program.LiveControl.Pause(ts);
                            else
                                Program.LiveControl.Pause();
                        }
                        else
                        {
                            Program.LiveControl.Pause();
                        }

                        break;
                    case "resume":
                        Program.LiveControl.Resume();
                        break;

                    case "stop":
                        break;

                    case "abort":
                        break;

                    case "run":
                    case "run-backup":
                        {
                            Duplicati.Server.Serialization.Interface.IBackup backup = null;
                            if (long.TryParse(input["id"].Value, out id))
                                backup = Program.DataConnection.GetBackup(id);

                            if (backup == null)
                            {
                                ReportError(response, bw, string.Format("No backup found for id: {0}", input["id"].Value));
                                return;
                            }

                            Program.WorkThread.AddTask(new Tuple<long, DuplicatiOperation>(id, DuplicatiOperation.Backup));
                        }
                        break;
                    case "clear-warning":
                        Program.HasWarning = false;
                        break;
                    case "clear-error":
                        Program.HasError = false;
                        break;
                    
                    default:
                        ReportError(response, bw, string.Format("Unsupported command {0}", command));
                        break;
                }

                OutputObject(bw, new { Status = "OK" });
            }
        }
    }
}
