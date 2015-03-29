﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MediaBrowser.Model.ApiClient;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using MediaBrowser.Theater.Api;
using MediaBrowser.Theater.Api.Library;
using MediaBrowser.Theater.Api.Navigation;
using MediaBrowser.Theater.Api.Playback;
using MediaBrowser.Theater.Api.Session;
using MediaBrowser.Theater.Api.UserInterface;
using MediaBrowser.Theater.DefaultTheme.Core.ViewModels;
using MediaBrowser.Theater.DefaultTheme.Home.ViewModels.Movies;
using MediaBrowser.Theater.DefaultTheme.ItemList;
using MediaBrowser.Theater.Playback;
using MediaBrowser.Theater.Presentation;
using MediaBrowser.Theater.Presentation.Controls;
using MediaBrowser.Theater.Presentation.ViewModels;

namespace MediaBrowser.Theater.DefaultTheme.Home.ViewModels.TV
{
    public class TvSpotlightViewModel
        : BaseViewModel, IKnownSize, IHomePage
    {        
        private readonly IImageManager _imageManager;
        private readonly ILogger _logger;
        private readonly double _miniSpotlightWidth;
        private readonly INavigator _navigator;
        private readonly IConnectionManager _connectionManager;
        private readonly IPlaybackManager _playbackManager;
        private readonly ISessionManager _sessionManager;
        private CancellationTokenSource _mainViewCancellationTokenSource;

        public TvSpotlightViewModel(BaseItemDto tvFolder, IImageManager imageManager, INavigator navigator, IConnectionManager connectionManager, ISessionManager sessionManager, ILogManager logManager, IPlaybackManager playbackManager)
        {
            _imageManager = imageManager;
            _navigator = navigator;
            _connectionManager = connectionManager;
            _playbackManager = playbackManager;
            _sessionManager = sessionManager;
            _logger = logManager.GetLogger("TV Spotlight");
            SpotlightHeight = HomeViewModel.TileHeight*2 + HomeViewModel.TileMargin*2;
            SpotlightWidth = 16*(SpotlightHeight/9) + 100;
            _miniSpotlightWidth = HomeViewModel.TileWidth + (HomeViewModel.TileMargin/4) - 1;

            Title = SectionTitle = tvFolder.Name;

            LowerSpotlightWidth = SpotlightWidth/2 - HomeViewModel.TileMargin;
            LowerSpotlightHeight = HomeViewModel.TileHeight;

            AllShowsCommand = new RelayCommand(arg => {
                var itemParams = new ItemListParameters {
                    Title = tvFolder.Name,
                    Items = ItemChildren.Get(connectionManager, sessionManager, tvFolder, new ChildrenQueryParams {
                        ExpandSingleItems = true,
                        Recursive = true,
                        IncludeItemTypes = new[] { "Series" }
                    })
                };

                navigator.Navigate(Go.To.ItemList(itemParams));
            });

            GenresCommand = new RelayCommand(arg => {
                var itemParams = new ItemListParameters {
                    Title = "Genres",
                    Items = connectionManager.GetApiClient(tvFolder).GetGenresAsync(new ItemsByNameQuery { ParentId = tvFolder.Id, UserId = sessionManager.CurrentUser.Id })
                };

                navigator.Navigate(Go.To.ItemList(itemParams));
            });

            SpotlightViewModel = new ItemSpotlightViewModel(imageManager, _connectionManager) {
                ImageType = ImageType.Backdrop,
                ItemSelectedAction = i => navigator.Navigate(Go.To.Item(i))
            };

            AllShowsImagesViewModel = new ImageSlideshowViewModel(imageManager, Enumerable.Empty<string>()) {
                ImageStretch = Stretch.UniformToFill
            };

            MiniSpotlightItems = new RangeObservableCollection<ItemTileViewModel> {
                CreateMiniSpotlightItem(),
                CreateMiniSpotlightItem(),
                CreateMiniSpotlightItem(),
            };

            LoadViewModels(tvFolder);
        }

        public double SpotlightWidth { get; private set; }
        public double SpotlightHeight { get; private set; }

        public double LowerSpotlightWidth { get; private set; }
        public double LowerSpotlightHeight { get; private set; }

        public ItemSpotlightViewModel SpotlightViewModel { get; private set; }
        public RangeObservableCollection<ItemTileViewModel> MiniSpotlightItems { get; private set; }
        public ImageSlideshowViewModel AllShowsImagesViewModel { get; private set; }
        public ICommand AllShowsCommand { get; private set; }
        public ICommand GenresCommand { get; private set; }
        
        public string Title { get; private set; }

        public string SectionTitle { get; private set; }

        public int Index { get; set; }

        public Size Size
        {
            get
            {
                return new Size(SpotlightWidth + _miniSpotlightWidth + 4*HomeViewModel.TileMargin + HomeViewModel.SectionSpacing,
                                SpotlightHeight + HomeViewModel.TileHeight + 4*HomeViewModel.TileMargin);
            }
        }

        private void DisposeMainViewCancellationTokenSource(bool cancel)
        {
            if (_mainViewCancellationTokenSource != null) {
                if (cancel) {
                    try {
                        _mainViewCancellationTokenSource.Cancel();
                    }
                    catch (ObjectDisposedException) { }
                }
                _mainViewCancellationTokenSource.Dispose();
                _mainViewCancellationTokenSource = null;
            }
        }
        
        private async void LoadViewModels(BaseItemDto tvFolder)
        {
            CancellationTokenSource cancellationSource = _mainViewCancellationTokenSource = new CancellationTokenSource();

            try {
                cancellationSource.Token.ThrowIfCancellationRequested();

                var spotlight = await ItemChildren.Get(_connectionManager, _sessionManager, tvFolder, new ChildrenQueryParams {
                    Filters = new[] { ItemFilter.IsUnplayed },
                    IncludeItemTypes = new[] { "Series" },
                    SortBy = new[] { ItemSortBy.CommunityRating },
                    SortOrder = SortOrder.Descending,
                    Limit = 20,
                    Recursive = true
                });

                if (spotlight.TotalRecordCount < 10) {
                    spotlight = await ItemChildren.Get(_connectionManager, _sessionManager, tvFolder, new ChildrenQueryParams {
                        Recursive = true
                    });
                }

                LoadSpotlightViewModel(spotlight);
                await LoadAllShowsViewModel(tvFolder);
                LoadMiniSpotlightsViewModel(spotlight);
            }
            catch (Exception ex) {
                _logger.ErrorException("Error getting tv view", ex);
            }
            finally {
                DisposeMainViewCancellationTokenSource(false);
            }
        }

        private ItemTileViewModel CreateMiniSpotlightItem()
        {
            return new ItemTileViewModel(_connectionManager, _imageManager, _navigator, _playbackManager, null) {
                DesiredImageWidth = _miniSpotlightWidth,
                DesiredImageHeight = HomeViewModel.TileHeight,
                PreferredImageTypes = new[] { ImageType.Backdrop, ImageType.Thumb },
                DisplayNameGenerator = i => i.GetDisplayName(new DisplayNameFormat(true, true))
            };
        }

        private void LoadMiniSpotlightsViewModel(ItemsResult tvItems)
        {
            BaseItemDto[] items = tvItems.Items.Skip(5).Shuffle().Take(3).ToArray();

            for (int i = 0; i < items.Length; i++) {
                if (MiniSpotlightItems.Count > i) {
                    MiniSpotlightItems[i].Item = items[i];
                } else {
                    ItemTileViewModel vm = CreateMiniSpotlightItem();
                    vm.Item = items[i];

                    MiniSpotlightItems.Add(vm);
                }
            }

            if (MiniSpotlightItems.Count > items.Length) {
                List<ItemTileViewModel> toRemove = MiniSpotlightItems.Skip(items.Length).ToList();
                MiniSpotlightItems.RemoveRange(toRemove);
            }
        }

        private void LoadSpotlightViewModel(ItemsResult tvItems)
        {
            SpotlightViewModel.Items = tvItems.Items.Where(i => i.BackdropImageTags.Any()).Take(5).Shuffle().ToArray(); ;
        }

        private async Task LoadAllShowsViewModel(BaseItemDto tvFolder)
        {
            var items = await ItemChildren.Get(_connectionManager, _sessionManager, tvFolder, new ChildrenQueryParams {
                Recursive = true,
                IncludeItemTypes = new[] { "Series" }
            });

            var apiClient = _connectionManager.GetApiClient(tvFolder);
            IEnumerable<string> images = items.Items
                                              .Where(i => i.BackdropImageTags.Any())
                                              .Select(i => apiClient.GetImageUrl(i.Id, new ImageOptions {
                                                  ImageType = ImageType.Backdrop,
                                                  Tag = i.BackdropImageTags.First(),
                                                  Height = Convert.ToInt32(HomeViewModel.TileWidth*2),
                                                  EnableImageEnhancers = false
                                              }));

            AllShowsImagesViewModel.Images.AddRange(images.Shuffle());
            AllShowsImagesViewModel.StartRotating();
        }
    }
}