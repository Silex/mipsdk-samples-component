﻿// StatusSessionConsole. Milestone XProtect MIP SDK sample Program
// Connects to an XProtect Corporate Management Server to log on and obtain a token plus system information
// Then connects to one XProtect Corporate Recording Server to pass the token and query status information

using System;
using System.Collections.Generic;
using System.Net;
using VideoOS.Platform;
using System.Linq;
using VideoOS.Platform.SDK.Config;
using VideoOS.Platform.SDK.StatusClient;
using VideoOS.Platform.SDK.StatusClient.StatusEventArgs;

namespace StatusSessionConsole
{
    class Program
    {
        private static IDictionary<Guid, Item> _cameras;
        private static IDictionary<Guid, Item> _hardware = new Dictionary<Guid, Item>();

        private static readonly Guid IntegrationId = new Guid("FF0B9F27-A2C2-4720-989B-9AB0509BA099");
        private const string IntegrationName = "Status Session Console";
        private const string Version = "1.0";
        private const string ManufacturerName = "Sample Manufacturer";

        // Take name of XPCO Management Server from command line or use a hardcoded default.
        // This is the only value you must change to modify this to run on your site.
        static void Main(string[] args)
        {
            string server = "http://localhost";
            if (args.Length > 0)
            {
                server = args[0];
            }

            var recordingServer = GetRecordingServer(server);
            var recordingServerId = recordingServer.FQID;

            // Find all cameras connected to the Recording Server
            List<Item> allItems = recordingServer.GetChildren();
            _cameras = FindAllCameras(allItems, recordingServerId.ServerId.Id).ToDictionary(item => item.FQID.ObjectId);
            ISet<Guid> subsribedCameras = new HashSet<Guid>(_cameras.Keys);

            SearchResult result;
            List<Item> allUserDefinedEvents = Configuration.Instance.GetItemsBySearch(Kind.TriggerEvent.ToString(), 10, 5, out result);
            
            List<Item> allHardware = Configuration.Instance.GetItemsBySearch(Kind.Hardware.ToString(), 10, 5, out result);
            foreach (var h in allHardware)
            {
                if (h.FQID.FolderType == FolderType.No)
                    _hardware.Add(h.FQID.ObjectId, h);
            }
            // Subscribe to a set of known events
            ISet<Guid> subscribedEvents = new HashSet<Guid>(KnownStatusEvents.GetAllEvents());
            foreach (Item udevent in allUserDefinedEvents)
                subscribedEvents.Add(udevent.FQID.ObjectId);

            ISet<Guid> subsribedHardware = new HashSet<Guid>();
            foreach (var hw in _hardware.Values)
                subsribedHardware.Add(hw.FQID.ObjectId);
            // Start the Status session with the Recording Server. The StatusSession class does more than just act as a proxy,
            // as it also handles reconnects and dropped sessions. The Dispose method is used to stop the session with the
            // Recording Server, so it should be called when the session is finished either explicitly or in a using-statement.
            Console.WriteLine("Press a key to terminate the sample");
            using (var statusApi = new StatusSession(recordingServer))
            {
                // Lets get hold of all events, including the dynamic ones defined by drivers
                foreach (var ev in statusApi.GetAllStatusEventMessages())
                {
                    subscribedEvents.Add(ev.Id);
                }
                // Listen for changes to the connection state
                statusApi.ConnectionStateChanged += ConnectionStateChangedHandler;

                // Listen to changes to the states of cameras
                statusApi.CameraStateChanged += CameraStateChangedHandler;
                statusApi.HardwareStateChanged += StatusApi_HardwareStateChanged;
                //statusApi.InputDeviceStateChanged += ...
                //statusApi.MicrophoneStateChanged += ...

                // Listen to events
                statusApi.EventFired += EventFiredHandler;

                // Start the session with the Recording Server
                statusApi.StartSession();

                // Subscribe to events
                statusApi.SetSubscribedEvents(subscribedEvents);

                // Subscribe to camera found
                statusApi.SetSubscribedDevicesForStateChanges(subsribedCameras);

                // Alternatively, it is possible to subscribe to all devices of a specific kind.
                //statusApi.AddSubscriptionsToDevicesOfKind(Kind.Camera);

                // Subscribe to all hardware
                statusApi.SetSubscribedHardwareForStateChanges(subsribedHardware);

                // Wait for events to arrive and stop the program when the user presses a key.
                Console.ReadKey();

                statusApi.EventFired -= EventFiredHandler;
                statusApi.CameraStateChanged -= CameraStateChangedHandler;
                statusApi.ConnectionStateChanged -= ConnectionStateChangedHandler;
            }

            Console.WriteLine("Terminating main normally...");
        }

        private static void StatusApi_HardwareStateChanged(object sender, HardwareStateChangedEventArgs e)
        {
            Item hardware;
            var sourceName = _hardware.TryGetValue(e.HardwareId, out hardware) ? hardware.Name : e.HardwareId.ToString();

            Console.WriteLine("{0} - Hardware: {1} states: Changed:{2}, Started:{3}, Error:{4}, ErrorNoConnection:{5}, ErrorNoLicence:{6}", e.Time.ToLocalTime(), sourceName, e.IsChange, e.Started, e.Error, e.ErrorNoConnection, e.ErrorNotLicensed);
        }
        private static void EventFiredHandler(object sender, EventFiredEventArgs e)
        {
            string sourceName = "Name-not-found";
            SearchResult result;
            List<Item> anyItem = Configuration.Instance.GetItemsBySearch(e.SourceId.ToString(), 10, 5, out result);
            if (anyItem.Count == 1)
                sourceName = anyItem[0].Name;

            Console.WriteLine("{0} - Event {1} fired from source {2}",
                e.Time.ToLocalTime(), KnownStatusEvents.GetEventName(e.EventId), sourceName);
            foreach (Guid id in e.DeviceIds)
            {
                String cameraName = "";
                Item camera;
                if (_cameras.TryGetValue(id, out camera)) cameraName = camera.Name;
                Console.WriteLine(" --- > " + id+ "  - "+cameraName);
            }
            foreach (string k in e.Metadata.Keys)
            {
                Console.WriteLine(" --- > Metadata: " + k + " = " + e.Metadata[k]);
            }
        }

        private static void ConnectionStateChangedHandler(object sender, ConnectionStateChangedEventArgs e)
        {
            Console.WriteLine("Connection state changed to: {0}", e.ConnectionState);
        }

        private static void CameraStateChangedHandler(object sender, CameraStateChangedEventArgs e)
        {
            Console.WriteLine(
                "{0} - Camera state changes for camera {1}: Started ({2}) Recording ({3}) Motion ({4}) NoConnection({5})",
                e.Time.ToLocalTime(),
                _cameras[e.DeviceId].Name,
                e.Started,
                e.Recording,
                e.Motion,
                e.ErrorNoConnection);
        }

        static Item GetRecordingServer(string hostname)
        {
            VideoOS.Platform.SDK.Environment.Initialize();

            string hostManagementService = hostname;
            if (hostManagementService.StartsWith("http://") == false)
                hostManagementService = "http://" + hostManagementService;

            Uri uri = new UriBuilder(hostManagementService).Uri;
            VideoOS.Platform.SDK.Environment.AddServer(uri, CredentialCache.DefaultNetworkCredentials);

            // If you need different credentials than the user that runs the sample, please comment out the line above and
            // uncomment the line below and set the appropriate username and password.
            //VideoOS.Platform.SDK.Environment.AddServer(uri, new NetworkCredential("username", "password"));

			try
			{
				VideoOS.Platform.SDK.Environment.Login(uri, IntegrationId, IntegrationName, Version, ManufacturerName);
			}
			catch (Exception ex)
			{
				Console.WriteLine("Could not logon to management server: " + ex.Message);
				Console.WriteLine("");
				Console.WriteLine("Press any key");
				Console.ReadKey();
                throw new ApplicationException("Cannot connect");
            }

            if (EnvironmentManager.Instance.CurrentSite.ServerId.ServerType != ServerId.CorporateManagementServerType)
            {
                Console.WriteLine("{0} is not an XProtect Corporate Management Server", hostManagementService);
                Console.WriteLine("");
                Console.WriteLine("Press any key");
                Console.ReadKey();
                throw new ApplicationException("Wrong servertype");
            }

            VideoOS.Platform.Login.LoginSettings loginSettings =
                VideoOS.Platform.Login.LoginSettingsCache.GetLoginSettings(hostManagementService);
            Console.WriteLine("... Token=" + loginSettings.Token);

            // Limit this to 1 recording server. Here I select the last one.
            // If there are XPE servers acting as recording servers, you must filter them away by having an explicit list
            // With XPE, you don't use the Status API, but the Central API". See the sample "CentralDemo".
            Item serverItem = Configuration.Instance.GetItem(EnvironmentManager.Instance.CurrentSite);
            List<Item> serverItems = serverItem.GetChildren();
            Item recorder = null;
            foreach (Item item in serverItems)
            {
                if (item.FQID.Kind == Kind.Server && item.FQID.ServerId.ServerType == ServerId.CorporateRecordingServerType)
                {
                    recorder = item;
                }
            }

            return recorder;
        }

        private static IEnumerable<Item> FindAllCameras(IEnumerable<Item> items, Guid recorderGuid)
        {
            foreach (Item item in items)
            {
                if (item.FQID.Kind == Kind.Camera && item.FQID.ParentId == recorderGuid && item.FQID.FolderType == FolderType.No)
                {
                    yield return item;
                }
                else if (item.FQID.FolderType != FolderType.No)
                {
                    foreach (var camera in FindAllCameras(item.GetChildren(), recorderGuid))
                    {
                        yield return camera;
                    }
                }
            }
        }
    }
}
