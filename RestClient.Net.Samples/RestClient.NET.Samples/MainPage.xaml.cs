﻿using Atlassian;
using RestClient.Net.Samples.Model;
using RestClient.Net.UnitTests.Model;
using RestClientDotNet.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ThomasBayer;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace RestClientDotNet.Sample
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class MainPage
    {
        #region Fields
        private RestClient _BitbucketClient;
        #endregion

        #region Constructror
        public MainPage()
        {
            InitializeComponent();
            AttachEventHandlers();
        }

        #endregion

        #region Private Methods

        private async Task<CUSTOMER> GetCustomer(int customerId)
        {
            CUSTOMER retVal = null;

            ToggleBusy(true);
            try
            {
                var countryCodeClient = new RestClient(new XmlSerializationAdapter(), new Uri("http://www.thomas-bayer.com/sqlrest/CUSTOMER/" + customerId));
                retVal = await countryCodeClient.GetAsync<CUSTOMER>();
            }
            catch (Exception ex)
            {
                await HandleException(ex);
            }

            ToggleBusy(false);
            return retVal;
        }

        private void GetBitBucketClient(string password, bool isGet)
        {
            var url = "https://api.bitbucket.org/2.0/repositories/" + UsernameBox.Text;
            _BitbucketClient = new RestClient(new NewtonsoftSerializationAdapter(), new Uri(url));

            if (!string.IsNullOrEmpty(password))
            {
                _BitbucketClient.UseBasicAuthentication(UsernameBox.Text, password);
            }
        }

        private async void OnGetReposClick()
        {
            ToggleBusy(true);

            try
            {
                ReposBox.ItemsSource = null;
                ReposBox.IsEnabled = false;

                //Ensure the client is ready to go
                GetBitBucketClient(GetPassword(), true);

                //Download the repository data
                RepositoryList repos = (await _BitbucketClient.GetAsync<RepositoryList>());

                //Put it in the List Box
                ReposBox.ItemsSource = repos.values;
                ReposBox.IsEnabled = true;
            }
            catch (Exception ex)
            {
                await HandleException(ex);
            }

            ToggleBusy(false);
        }

        private async Task OnSavedClicked()
        {
            ToggleBusy(true);

            try
            {
                var selectedRepo = ReposBox.SelectedItem as Repository;
                if (selectedRepo == null)
                {
                    return;
                }

                //Ensure the client is ready to go
                GetBitBucketClient(GetPassword(), false);

                var requestUri = $"{UsernameBox.Text}/{selectedRepo.full_name.Split('/')[1]}";

                //Put the change
                var retVal = await _BitbucketClient.PutAsync<Repository, Repository>(selectedRepo, requestUri);

                await DisplayAlert("Saved", "Your repo was updated.");
            }
            catch (Exception ex)
            {
                await HandleException(ex);
            }

            ToggleBusy(false);
        }

        private async void OnCountryCodeGridLoaded()
        {
            ToggleBusy(true);

            try
            {
                var baseUri = new Uri("https://restcountries.eu/rest/v2/");
                var restClient = new RestClient(new NewtonsoftSerializationAdapter(), baseUri);
                List<RestCountry> countries = await restClient.GetAsync<List<RestCountry>>();
                CountryCodeList.ItemsSource = countries;
            }
            catch (Exception ex)
            {
                await HandleException(ex);
            }

            ToggleBusy(false);
        }

        private async Task HandleException(Exception ex)
        {
            ErrorModel errorModel = null;

            var hex = ex as HttpStatusException;
            if (hex != null)
            {
                errorModel = await hex.RestResponse.ReadResponseAsync<ErrorModel>();
            }

            var message = $"An error occurred while attempting to use a REST service.\r\nError: {ex.Message}\r\nInner Error: {ex.InnerException?.Message}\r\nInner Inner Error: {ex.InnerException?.InnerException?.Message}";

            if (errorModel != null)
            {
                message += $"\r\n{errorModel.error.message}";
                message += $"\r\nStatus Code: {hex.RestResponse.StatusCode}";
            }

            await DisplayAlert("Error", message);
        }

        private string GetPassword()
        {
            return ThePasswordBox.Text;
        }

        private async Task DisplayAlert(string title, string message)
        {
            await DisplayAlert(title, message, "OK");
        }

        private void ToggleBusy(bool isBusy)
        {
            ReposActivityIndicator.IsVisible = isBusy;
            CountryCodesActivityIndicator.IsVisible = isBusy;
            ReposActivityIndicator.IsRunning = isBusy;
            CountryCodesActivityIndicator.IsRunning = isBusy;
        }

        private void AttachEventHandlers()
        {
            SaveButton.Clicked += SaveButton_Clicked;
            CurrentPageChanged += MainPage_CurrentPageChanged;
            GetReposButton.Clicked += GetReposButton_Clicked;
            ReposBox.ItemSelected += ReposBox_ItemSelected;
        }

        #endregion

        #region Event Handlers
        private void MainPage_CurrentPageChanged(object sender, EventArgs e)
        {
            if (CurrentPage == CountryCodesPage)
            {
                OnCountryCodeGridLoaded();
            }
        }

        private void ReposBox_ItemSelected(object sender, SelectedItemChangedEventArgs e)
        {
            ReposPage.BindingContext = ReposBox.SelectedItem as Repository;
        }

        private async void SaveButton_Clicked(object sender, EventArgs e)
        {
            await OnSavedClicked();
        }

        private void GetReposButton_Clicked(object sender, EventArgs e)
        {
            OnGetReposClick();
        }

        private async void Delete_Clicked(object sender, EventArgs e)
        {
            var restClient = new RestClient(new NewtonsoftSerializationAdapter(), new Uri("https://jsonplaceholder.typicode.com"));
            await restClient.DeleteAsync("/posts/1");
            await DisplayAlert("Post Deleted", $"The server pretended to delete the post 1");
        }

        private async void Patch_Clicked(object sender, EventArgs e)
        {
            var restClient = new RestClient(new NewtonsoftSerializationAdapter(), new Uri("https://jsonplaceholder.typicode.com"));
            UserPost userPost = await restClient.PatchAsync<UserPost, UserPost>(new UserPost { title = "Moops" }, "/posts/1");
            await DisplayAlert("Post Patched", $"The server pretended to patch a post titled:\r\n{userPost.title}");
        }

        private async void Post_Clicked(object sender, EventArgs e)
        {
            var restClient = new RestClient(new NewtonsoftSerializationAdapter(), new Uri("https://jsonplaceholder.typicode.com"));
            UserPost userPost = await restClient.PostAsync<UserPost, UserPost>(new UserPost { title = "Moops" }, "/posts");
            await DisplayAlert("Post made", $"The server pretended to accept the post:\r\n{userPost.title}");
        }

        private async void PostWithCancellation_Clicked(object sender, EventArgs e)
        {
            try
            {
                var restClient = new RestClient(new NewtonsoftSerializationAdapter(), new Uri("https://jsonplaceholder.typicode.com"));

                var tokenSource = new CancellationTokenSource();
                var token = tokenSource.Token;

                var task = restClient.PostAsync<UserPost, UserPost>(new UserPost { title = "Moops" }, new Uri("/posts", UriKind.Relative), token);

                tokenSource.Cancel();

                await task;
            }
            catch (OperationCanceledException ex)
            {
                await DisplayAlert("Cancellation", ex.Message);
            }
            catch (Exception)
            {
                await DisplayAlert("Error", "Cancellation didn't work :(");
            }
        }

        private async void PostWithTimeout_Clicked(object sender, EventArgs e)
        {
            try
            {
                var restClient = new RestClient(new NewtonsoftSerializationAdapter(), new Uri("https://jsonplaceholder.typicode.com"), new TimeSpan(0, 0, 0, 0, 1));
                await restClient.PostAsync<UserPost, UserPost>(new UserPost { title = "Moops" }, new Uri("/posts", UriKind.Relative));
            }
            catch (OperationCanceledException ex)
            {
                await DisplayAlert("Cancellation", ex.Message);
            }
            catch (Exception)
            {
                await DisplayAlert("Error", "Cancellation didn't work :(");
            }
        }

        #endregion
    }
}
