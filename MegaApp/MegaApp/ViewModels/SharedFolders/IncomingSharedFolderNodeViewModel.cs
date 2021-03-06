﻿using mega;
using MegaApp.Classes;
using MegaApp.Interfaces;
using MegaApp.MegaApi;
using MegaApp.Services;
using MegaApp.ViewModels.Dialogs;

namespace MegaApp.ViewModels.SharedFolders
{
    public class IncomingSharedFolderNodeViewModel : SharedFolderNodeViewModel, IMegaIncomingSharedFolderNode
    {
        public IncomingSharedFolderNodeViewModel(MNode megaNode, SharedFoldersListViewModel parent)
            : base(megaNode, parent)
        {
            this.LeaveShareCommand = new RelayCommand(LeaveShare);

            this.DefaultImagePathData = ResourceService.VisualResources.GetString("VR_IncomingSharedFolderPathData");
            this.Update(megaNode);            
        }

        #region Methods

        /// <summary>
        /// Update core data associated with the SDK MNode object
        /// </summary>
        /// <param name="megaNode">Node to update</param>
        /// <param name="externalUpdate">Indicates if is an update external to the app. For example from an `onNodesUpdate`</param>
        public override async void Update(MNode megaNode, bool externalUpdate = false)
        {
            base.Update(megaNode, externalUpdate);

            var owner = SdkService.MegaSdk.getUserFromInShare(megaNode);
            var contactAttributeRequestListener = new GetUserAttributeRequestListenerAsync();
            var firstName = await contactAttributeRequestListener.ExecuteAsync(() =>
                SdkService.MegaSdk.getUserAttribute(owner, (int)MUserAttrType.USER_ATTR_FIRSTNAME,
                contactAttributeRequestListener));
            var lastName = await contactAttributeRequestListener.ExecuteAsync(() =>
                SdkService.MegaSdk.getUserAttribute(owner, (int)MUserAttrType.USER_ATTR_LASTNAME,
                contactAttributeRequestListener));

            OnUiThread(() =>
            {
                this.Owner = string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName) ?
                    owner.getEmail() : string.Format("{0} {1}", firstName, lastName);
            });
        }        

        public async void LeaveShare()
        {
            if (this.Parent.ItemCollection.IsMultiSelectActive)
            {
                if (this.Parent.LeaveShareCommand.CanExecute(null))
                    this.Parent.LeaveShareCommand.Execute(null);
                return;
            }

            var dialogResult = await DialogService.ShowOkCancelAsync(
                ResourceService.AppMessages.GetString("AM_LeaveSharedFolder_Title"),
                string.Format(ResourceService.AppMessages.GetString("AM_LeaveSharedFolderQuestion"), this.Name),
                ResourceService.AppMessages.GetString("AM_LeaveSharedFolderWarning"),
                TwoButtonsDialogType.Custom,
                ResourceService.UiResources.GetString("UI_Leave"), this.CancelText);

            if (!dialogResult) return;

            if (!await this.RemoveAsync())
            {
                OnUiThread(async () =>
                {
                    await DialogService.ShowAlertAsync(
                        ResourceService.AppMessages.GetString("AM_LeaveSharedFolder_Title"),
                        string.Format(ResourceService.AppMessages.GetString("AM_LeaveSharedFolderFailed"), this.Name));
                });
            }
        }

        #endregion

        #region Properties

        private string _owner;
        /// <summary>
        /// Owner of the incoming shared folder
        /// </summary>
        public override string Owner
        {
            get { return _owner; }
            set { SetField(ref _owner, value); }
        }

        public bool AllowRename => !this.Parent.ItemCollection.IsMultiSelectActive && this.HasFullAccessPermissions;

        public bool IsEnabledForCopyMoveImport => this.Parent.IsForSelectFolder && this.HasReadWritePermissions;

        #endregion

        #region UiResources

        public string LeaveShareText => ResourceService.UiResources.GetString("UI_LeaveShare");

        #endregion
    }
}
