﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Storage;
using BackgroundTaskService.Enums;
using mega;
using BackgroundTaskService.MegaApi;
using BackgroundTaskService.Network;
using BackgroundTaskService.Services;

namespace BackgroundTaskService
{
    public sealed class CameraUploadTask : IBackgroundTask
    {
        // If you run any asynchronous code in your background task, then your background task needs to use a deferral. 
        // If you don't use a deferral, then the background task process can terminate unexpectedly
        // if the Run method completes before your asynchronous method call has completed.
        // Note: defined at class scope so we can mark it complete inside the OnCancel() callback if we choose to support cancellation
        private static BackgroundTaskDeferral _deferral;

        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            _deferral = taskInstance.GetDeferral();

            // Check for network changes
            NetworkService.CheckNetworkChange();

            // Set the API to use depending on the settings
            if (SettingsService.LoadSetting(ResourceService.SettingsResources.GetString("SR_UseStagingServer"), false))
                SdkService.MegaSdk.changeApiUrl("https://staging.api.mega.co.nz/");
            else if (SettingsService.LoadSetting(ResourceService.SettingsResources.GetString("SR_UseStagingServerPort444"), false))
                SdkService.MegaSdk.changeApiUrl("https://staging.api.mega.co.nz:444/", true);
            else
                SdkService.MegaSdk.changeApiUrl("https://g.api.mega.co.nz/");

            // Log message to indicate that the service is invoked
            LogService.Log(MLogLevel.LOG_LEVEL_INFO, "Service invoked.");

            // Load the connection upload settings
            CameraUploadsConnectionType cameraUploadsConnectionType = CameraUploadsConnectionType.EthernetWifiOnly;
            try
            {
                cameraUploadsConnectionType = (CameraUploadsConnectionType) await SettingsService.LoadSettingFromFileAsync<int>("CameraUploadsSettingsHowKey");
            }
            catch (Exception e)
            {
                LogService.Log(MLogLevel.LOG_LEVEL_ERROR, "Could not load settings", e);
            }            

            if (!CheckNetWork(cameraUploadsConnectionType))
            {
                LogService.Log(MLogLevel.LOG_LEVEL_INFO, "Task finished: connection type");
                _deferral.Complete();
                return;
            }

            SdkService.InitializeSdkParams();

            var loggedIn = await LoginAsync();

            if (loggedIn)
            {
                var fetched = await FetchNodesAsync();
                if (fetched)
                {
                    // Add notifications listener
                    var megaGlobalListener = new MegaGlobalListener();
                    SdkService.MegaSdk.addGlobalListener(megaGlobalListener);

                    // Enable the transfers resumption for the Camera Uploads service
                    await megaGlobalListener.ExecuteAsync(() => SdkService.MegaSdk.enableTransferResumption());

                    var cameraUploadRootNode = await SdkService.GetCameraUploadRootNodeAsync();
                    if (cameraUploadRootNode == null)
                    {
                        // No camera upload node found or created
                        // Just finish this run and try again next time
                        LogService.Log(MLogLevel.LOG_LEVEL_ERROR, "No Camera Uploads folder detected/created");
                        _deferral.Complete();
                        return;
                    }

                    // Load the file upload settings
                    CameraUploadsFileType cameraUploadsFileType = CameraUploadsFileType.PhotoAndVideo;
                    try
                    {
                        cameraUploadsFileType = (CameraUploadsFileType)await SettingsService.LoadSettingFromFileAsync<int>("CameraUploadsSettingsFileKey");
                    }
                    catch (Exception e)
                    {
                        LogService.Log(MLogLevel.LOG_LEVEL_ERROR, "Could not load settings", e);
                    }

                    var uploadFolders = new List<StorageFolder>();
                    switch (cameraUploadsFileType)
                    {
                        case CameraUploadsFileType.PhotoAndVideo:
                            uploadFolders.Add(KnownFolders.PicturesLibrary);
                            uploadFolders.Add(KnownFolders.VideosLibrary);
                            break;
                        case CameraUploadsFileType.PhotoOnly:
                            uploadFolders.Add(KnownFolders.PicturesLibrary);
                            break;
                        case CameraUploadsFileType.VideoOnly:
                            uploadFolders.Add(KnownFolders.VideosLibrary);
                            break;
                    }

                    // Get the IMAGE and/or VIDEO files to upload to MEGA
                    var fileToUpload = await TaskService.GetAvailableUploadAsync(
                            TaskService.ImageDateSetting, uploadFolders.ToArray());
                    foreach (var storageFile in fileToUpload)
                    {
                        // Skip the current file if it has failed more than the max error count
                        if(await ErrorHandlingService.SkipFileAsync(
                            storageFile.Name,
                            ErrorHandlingService.ImageErrorFileSetting, 
                            ErrorHandlingService.ImageErrorCountSetting)) continue;

                        if (!CheckNetWork(cameraUploadsConnectionType))
                        {
                            break;
                        }

                        // Calculate time for fingerprint check and upload
                        ulong mtime = TaskService.CalculateMtime(storageFile.DateCreated.DateTime);
                        try
                        {
                            using (var fs = await storageFile.OpenStreamForReadAsync())
                            {
                                var isUploaded = SdkService.IsAlreadyUploaded(storageFile, fs, cameraUploadRootNode, mtime);
                                if (isUploaded)
                                {
                                    await TaskService.SaveLastUploadDateAsync(storageFile, TaskService.ImageDateSetting);
                                    continue;
                                }
                                await UploadAsync(storageFile, fs, cameraUploadRootNode, mtime);
                                // No error, clear error storage
                                await ErrorHandlingService.ClearAsync(ErrorHandlingService.ImageErrorFileSetting,
                                    ErrorHandlingService.ImageErrorCountSetting);
                            }
                        }
                        catch (OutOfMemoryException e)
                        {
                            // Something went wrong (could be memory limit)
                            // Just finish this run and try again next time
                            LogService.Log(MLogLevel.LOG_LEVEL_ERROR, "Out of memory while uploading", e);
                            break;
                        }
                        catch (Exception e)
                        {
                            LogService.Log(MLogLevel.LOG_LEVEL_ERROR, "Error uploading item", e);
                            await ErrorHandlingService.SetFileErrorAsync(storageFile.Name, 
                                ErrorHandlingService.ImageErrorFileSetting, ErrorHandlingService.ImageErrorCountSetting);
                        }
                    }
                }
                else
                {
                    LogService.Log(MLogLevel.LOG_LEVEL_ERROR, "Failed to fetch nodes");
                }
            }
            else
            {
                LogService.Log(MLogLevel.LOG_LEVEL_ERROR, "Failed to login");
            }
            
            _deferral.Complete();
        }

        /// <summary>
        /// Fetch the nodes from MEGA
        /// </summary>
        /// <returns>True if succeeded, else False</returns>
        private static async Task<bool> FetchNodesAsync()
        {
            var fetch = new MegaRequestListener<bool>();
            return await fetch.ExecuteAsync(() => SdkService.MegaSdk.fetchNodes(fetch));
        }

        /// <summary>
        /// Fastlogin to MEGA user account
        /// </summary>
        /// <returns>True if succeeded, else false</returns>
        private static async Task<bool> LoginAsync()
        {
            try
            {
                // Try to load shared session token
                var sessionToken = await SettingsService.LoadSessionFromLockerAsync();

                if (string.IsNullOrEmpty(sessionToken) || string.IsNullOrWhiteSpace(sessionToken))
                    throw new Exception("Session token is empty.");

                var login = new MegaRequestListener<bool>();
                return await login.ExecuteAsync(() => SdkService.MegaSdk.fastLogin(sessionToken, login));
            }
            catch (Exception e)
            {
                LogService.Log(MLogLevel.LOG_LEVEL_ERROR, "Error logging in", e);
                return false;
            }
        }

        private bool CheckNetWork(CameraUploadsConnectionType cameraUploadsConnectionType)
        {
            switch (cameraUploadsConnectionType)
            {
                case CameraUploadsConnectionType.EthernetWifiOnly:
                    return (NetworkHelper.Instance.ConnectionInformation.ConnectionType == ConnectionType.Ethernet ||
                           NetworkHelper.Instance.ConnectionInformation.ConnectionType == ConnectionType.WiFi) &&
                           !NetworkHelper.Instance.ConnectionInformation.IsInternetOnMeteredConnection;
                case CameraUploadsConnectionType.Any:
                    return true;
                default: return false;
            }
        }

        private static async Task UploadAsync(StorageFile fileToUpload, Stream fileStream, MNode rootNode, ulong mTime)
        {
            SdkService.MegaSdk.retryPendingConnections();

            // Make sure the stream pointer is at the start of the stream
            fileStream.Position = 0;

            // Create a temporary local path to save the picture for upload
            string tempFilePath = Path.Combine(TaskService.GetTemporaryUploadFolder(), fileToUpload.Name);

            // Copy file to local storage to be able to upload
            using (var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
            {
                // Set buffersize to avoid copy failure of large files
                await fileStream.CopyToAsync(fs, 8192);
                await fs.FlushAsync();
            }

            // Notify complete when storage quota exceeded error is raised in the transferlistener	
            // Notify complete will retry in the next task run
            var transfer = new MegaTransferListener();
            transfer.StorageQuotaExceeded += (sender, args) =>
            {
                _deferral.Complete();
            };

            // Init the upload
            var result = await transfer.ExecuteAsync(
                () => SdkService.MegaSdk.startUploadWithMtimeTempSource(tempFilePath, rootNode, mTime, true, transfer),
                TaskService.ImageDateSetting);

            if (!string.IsNullOrEmpty(result)) throw new Exception(result);
        }
    }
}
