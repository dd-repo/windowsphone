﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Windows.ApplicationModel.Activation;
using Windows.Storage.Pickers;
using mega;
using MegaApp.Classes;
using MegaApp.Enums;
using MegaApp.Models;
using MegaApp.Pages;
using MegaApp.Resources;

namespace MegaApp.Services
{
    static class FolderService
    {
        public static bool FolderExists(string path)
        {  
            return Directory.Exists(path);
        }

        public static int GetNumChildFolders(string path)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(path)) return 0;
                return Directory.GetDirectories(path).Length;
            }
            catch (Exception) { return 0; }            
        }

        public static int GetNumChildFiles(string path, bool isOfflineFolder = false)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(path)) return 0;

                string[] childFiles = Directory.GetFiles(path);
                if (childFiles == null) return 0;

                int num = 0;
                if (!isOfflineFolder)
                {
                    num = childFiles.Length;
                }
                else
                {
                    foreach (var filePath in childFiles)
                        if (!FileService.IsPendingTransferFile(Path.GetFileName(filePath))) num++;
                }

                return num;
            }
            catch (Exception) { return 0; }
        }

        public static bool IsEmptyFolder(string path)
        {
            return (Directory.GetDirectories(path).Count() == 0 && Directory.GetFiles(path).Count() == 0) ? true : false;
        }

        public static void CreateFolder(string path)
        {
            Directory.CreateDirectory(path);            
        }
        
        public static bool DeleteFolder(string path, bool recursive = false)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(path) && !Directory.Exists(path)) return false;
                Directory.Delete(path, recursive);
                return true;
            }
            catch (Exception e)
            {
                LogService.Log(MLogLevel.LOG_LEVEL_ERROR, "Error deleting folder.", e);
                return false;
            }
        }

        public static bool HasIllegalChars(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    LogService.Log(MLogLevel.LOG_LEVEL_WARNING, "Null or empty path.");
                    return true;
                }

                var invalidChars = Path.GetInvalidPathChars();
                foreach (var c in invalidChars)
                {
                    if (path.Contains(c.ToString()))
                    {
                        LogService.Log(MLogLevel.LOG_LEVEL_WARNING,
                            string.Format("Invalid character '{0}' in path '{1}'.", c.ToString(), path));
                        return true;
                    }
                }
                return false;
            }
            catch (Exception e)
            {
                LogService.Log(MLogLevel.LOG_LEVEL_ERROR, "Error checking invalid path characters.", e);
                return false;
            }
        }        

        public static bool Clear(string path)
        {
            try
            {
                if (HasIllegalChars(path))
                {
                    LogService.Log(MLogLevel.LOG_LEVEL_WARNING, string.Format("Error cleaning folder '{0}'.", path));
                    return false;
                }

                bool result = true;
                IEnumerable<string> foldersToDelete = Directory.GetDirectories(path);
                if (foldersToDelete != null)
                {
                    foreach (var folder in foldersToDelete)
                    {
                        if (folder != null)
                        {
                            if (HasIllegalChars(folder))
                            {
                                LogService.Log(MLogLevel.LOG_LEVEL_WARNING, string.Format("Error deleting folder '{0}'.", path));
                                result = false;
                                continue;
                            }

                            Directory.Delete(folder, true);
                        }
                    }
                }

                result = result & FileService.ClearFiles(Directory.GetFiles(path));

                return true;
            }
            catch (Exception e)
            {
                LogService.Log(MLogLevel.LOG_LEVEL_ERROR, "Error cleaning folder.", e);
                return false;
            }
        }

        public static bool IsOfflineRootFolder(string path)
        {
            if (!path.Trim().EndsWith("\\"))
                path = path.Insert(path.Length, "\\");

            return (String.Compare(AppService.GetDownloadDirectoryPath(), path) == 0) ? true : false;
        }

        #if WINDOWS_PHONE_81
        public static void SelectFolder(string operation, NodeViewModel nodeViewModel = null)
        {
            try
            {
                App.FileOpenOrFolderPickerOpenend = true;

                var folderPicker = new FolderPicker
                {
                    SuggestedStartLocation = PickerLocationId.Downloads
                };

                folderPicker.FileTypeFilter.Add("*");

                folderPicker.ContinuationData["Operation"] = operation;
                folderPicker.ContinuationData["NodeData"] = nodeViewModel != null ? nodeViewModel.Base64Handle : null;

                folderPicker.PickFolderAndContinue();
            }
            catch (Exception e)
            {
                Deployment.Current.Dispatcher.BeginInvoke(() =>
                {
                    new CustomMessageDialog(
                        AppMessages.SelectFolderFailed_Title,
                        String.Format(AppMessages.SelectFolderFailed, e.Message),
                        App.AppInformation,
                        MessageDialogButtons.Ok).ShowDialog();
                });
            }            
        }

        public static async Task<bool> SelectDownloadFolder(NodeViewModel nodeViewModel = null)
        {
            if (SettingsService.LoadSetting<bool>(SettingsResources.AskDownloadLocationIsEnabled, false))
            {
                // Ask the user a download location when alsways asking is ON
                SelectFolder("SelectDownloadFolder", nodeViewModel);
                return false;
            }

            if (SettingsService.LoadSetting<string>(SettingsResources.DefaultDownloadLocation, null) != null)
                return true;
            
            await DialogService.ShowOptionsDialog(UiResources.DownloadLocation, AppMessages.NoDownloadLocationSelected,
                new[]
                {
                    new DialogButton(UiResources.SelectFolder, () =>
                    {
                        // Ask the user a download location
                        SelectFolder("SelectDownloadFolder", nodeViewModel);
                    }),
                    new DialogButton(UiResources.Settings, () =>
                    {
                        // Go to preferences page
                        App.CloudDrive.PickerOrDialogIsOpen = false;
                        App.CloudDrive.NoFolderUpAction = true;
                        NavigateService.NavigateTo(typeof (SettingsPage), NavigationParameter.Normal);
                    })
                });
            return false;
        }

        public static void ContinueFolderOpenPicker(FolderPickerContinuationEventArgs args, FolderViewModel folderViewModel)
        {
            try
            {
                if (args == null || (args.ContinuationData["Operation"] as string) != "SelectDownloadFolder" || args.Folder == null)
                {
                    LogService.Log(MLogLevel.LOG_LEVEL_WARNING, "Error selecting the download destination folder");
                    ResetFolderPicker();
                    return;
                }

                if (!App.CloudDrive.IsUserOnline()) return;

                if (args.ContinuationData["NodeData"] != null)
                {
                    String base64Handle = (String)args.ContinuationData["NodeData"];
                    NodeViewModel node;
                    if (App.LinkInformation.PublicNode != null && base64Handle.Equals(App.LinkInformation.PublicNode.getBase64Handle()))
                    {
                        node = NodeService.CreateNew(SdkService.MegaSdk, App.AppInformation, App.LinkInformation.PublicNode, ContainerType.PublicLink);
                        App.LinkInformation.Reset();
                    }
                    else
                    {
                        node = NodeService.CreateNew(folderViewModel.MegaSdk, App.AppInformation,
                            folderViewModel.MegaSdk.getNodeByBase64Handle(base64Handle), folderViewModel.Type);
                    }

                    if (node != null)
                        node.Download(TransfersService.MegaTransfers, args.Folder.Path);

                    ResetFolderPicker();
                    return;
                }

                folderViewModel.MultipleDownload(args.Folder.Path);
            }
            catch (Exception e)
            {
                LogService.Log(MLogLevel.LOG_LEVEL_ERROR, "Error preparing downloads", e);
                new CustomMessageDialog(AppMessages.AM_DownloadFailed_Title, 
                    AppMessages.AM_PrepareDownloadsFailed, App.AppInformation, 
                    MessageDialogButtons.Ok).ShowDialog();
            }
            finally
            {
                ResetFolderPicker();
            }
        }

        private static void ResetFolderPicker()
        {
            App.AppInformation.PickerOrAsyncDialogIsOpen = false;

            // Reset the picker data
            var app = Application.Current as App;
            if (app != null) app.FolderPickerContinuationArgs = null;            
        }
        #endif
    }    
}
