﻿using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using MegaApp.Classes;
using MegaApp.Enums;
using MegaApp.Interfaces;
using MegaApp.MegaApi;
using MegaApp.Services;

namespace MegaApp.ViewModels.Contacts
{
    public class ContactRequestsListViewModel : ContactsBaseViewModel<IMegaContactRequest>
    {
        /// <summary>
        /// View model to manage a contact requests list
        /// </summary>
        /// <param name="isOutgoing">Indicate the contact request list is for outgoing requests or not</param>
        public ContactRequestsListViewModel(bool isOutgoing) : base(isOutgoing)
        {
            this.isOutgoing = isOutgoing;
            this.ContentType = this.isOutgoing ? ContactsContentType.OutgoingRequests : ContactsContentType.IncomingRequests;
            this.List = new CollectionViewModel<IMegaContactRequest>();

            this.AddContactCommand = new RelayCommand(AddContact);
            this.AcceptContactRequestCommand = new RelayCommand(AcceptContactRequest);
            this.CancelContactRequestCommand = new RelayCommand(CancelContactRequest);
            this.DeclineContactRequestCommand = new RelayCommand(DeclineContactRequest);
            this.RemindContactRequestCommand = new RelayCommand(RemindContactRequest);
            this.InvertOrderCommand = new RelayCommand(InvertOrder);

            this.CurrentOrder = ContactsSortOptions.EmailAscending;          
        }

        #region Commands

        public override ICommand AddContactCommand { get; }
        
        public override ICommand AcceptContactRequestCommand { get; }
        public override ICommand CancelContactRequestCommand { get; }
        public override ICommand DeclineContactRequestCommand { get; }
        public override ICommand RemindContactRequestCommand { get; }

        public override ICommand InvertOrderCommand { get; }

        #endregion

        #region Methods

        public void Initialize(GlobalListener globalListener)
        {
            this.GetMegaContactRequests();

            if (globalListener == null) return;
            if (isOutgoing)
                globalListener.OutgoingContactRequestUpdated += (sender, args) => this.GetMegaContactRequests();
            else
                globalListener.IncomingContactRequestUpdated += (sender, args) => this.GetMegaContactRequests();
        }

        public void Deinitialize(GlobalListener globalListener)
        {
            if (globalListener == null) return;
            if (isOutgoing)
                globalListener.OutgoingContactRequestUpdated -= (sender, args) => this.GetMegaContactRequests();
            else
                globalListener.IncomingContactRequestUpdated -= (sender, args) => this.GetMegaContactRequests();
        }

        private void ListOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(this.List.Items))
            {
                OnPropertyChanged(nameof(this.OrderTypeAndNumberOfItems));
                OnPropertyChanged(nameof(this.OrderTypeAndNumberOfSelectedItems));
            }
        }

        public async void GetMegaContactRequests()
        {
            // User must be online to perform this operation
            if (!IsUserOnline()) return;

            await OnUiThreadAsync(() => this.List.Clear());

            var contactRequestsList = isOutgoing ?
                SdkService.MegaSdk.getOutgoingContactRequests() : 
                SdkService.MegaSdk.getIncomingContactRequests();

            var contactRequestsListSize = contactRequestsList.size();

            for (int i = 0; i < contactRequestsListSize; i++)
            {
                // To avoid null values
                if (contactRequestsList.get(i) == null) continue;

                var contactRequest = new ContactRequestViewModel(contactRequestsList.get(i), this);
                await OnUiThreadAsync(() => this.List.Items.Add(contactRequest));
            }

            this.SortBy(this.CurrentOrder);
        }

        private void AddContact()
        {
            this.OnAddContactTapped();
        }

        private void AcceptContactRequest()
        {
            if (!this.List.HasSelectedItems) return;

            // Use a temp variable to avoid InvalidOperationException
            var selectedContactRequests = this.List.SelectedItems.ToList();

            foreach (var contactRequest in selectedContactRequests)
                contactRequest.AcceptContactRequest();
        }

        private void DeclineContactRequest()
        {
            if (!this.List.HasSelectedItems) return;

            // Use a temp variable to avoid InvalidOperationException
            var selectedContactRequests = this.List.SelectedItems.ToList();

            foreach (var contactRequest in selectedContactRequests)
                contactRequest.DeclineContactRequest();
        }

        private void RemindContactRequest()
        {
            if (!this.List.HasSelectedItems) return;

            // Use a temp variable to avoid InvalidOperationException
            var selectedContactRequests = this.List.SelectedItems.ToList();

            foreach (var contactRequest in selectedContactRequests)
                contactRequest.RemindContactRequest();
        }

        private void CancelContactRequest()
        {
            if (!this.List.HasSelectedItems) return;

            // Use a temp variable to avoid InvalidOperationException
            var selectedContactRequests = this.List.SelectedItems.ToList();

            foreach (var contactRequest in selectedContactRequests)
                contactRequest.CancelContactRequest();
        }

        public void SortBy(ContactsSortOptions sortOption)
        {
            switch (sortOption)
            {
                case ContactsSortOptions.EmailAscending:
                    OnUiThread(() =>
                    {
                        this.List.Items = new ObservableCollection<IMegaContactRequest>(
                            this.List.Items.OrderBy(item => this.isOutgoing ?
                            item.TargetEmail : item.SourceEmail));
                    });
                    break;

                case ContactsSortOptions.EmailDescending:
                    OnUiThread(() =>
                    {
                        this.List.Items = new ObservableCollection<IMegaContactRequest>(
                            this.List.Items.OrderByDescending(item => this.isOutgoing ?
                            item.TargetEmail : item.SourceEmail));
                    });
                    break;

                default:
                    return;
            }
        }

        private void InvertOrder()
        {
            switch (this.CurrentOrder)
            {
                case ContactsSortOptions.EmailAscending:
                    this.CurrentOrder = ContactsSortOptions.EmailDescending;
                    break;
                case ContactsSortOptions.EmailDescending:
                    this.CurrentOrder = ContactsSortOptions.EmailAscending;
                    break;
                default:
                    return;
            }

            this.SortBy(this.CurrentOrder);
        }

        #endregion

        #region Properties

        private bool isOutgoing { get; set; }

        #endregion
    }
}
