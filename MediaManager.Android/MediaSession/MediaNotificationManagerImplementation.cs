using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Media;
using Android.Support.V4.App;
using Android.Support.V4.Content;
using Android.Support.V4.Media;
using Android.Support.V4.Media.Session;
using Plugin.MediaManager.Abstractions;
using Plugin.MediaManager.Abstractions.Enums;
using Plugin.MediaManager.Abstractions.Implementations;
using Plugin.MediaManager.MediaSession;
using NotificationCompat = Android.Support.V7.App.NotificationCompat;

namespace Plugin.MediaManager
{
    public class MediaNotificationManagerImplementation : IMediaNotificationManager
    {
        //The user can set these on the application start to use custom icons
        public static int PreviousIconDrawableId = 0;
        public static int NextIconDrawableId = 0;
        public static int PlayIconDrawableId = 0;
        public static int PauseIconDrawableId = 0;
        public static int SmallIconDrawableId = 0;
        //The user can set this to control if an album art is displayed on the lock screen in fullscreen
        public static bool ShowFullscreenAlbumArtInLockscreen = true;



        // private MediaSessionManagerImplementation _sessionHandler;
        private Intent _intent;
        private PendingIntent _pendingCancelIntent;
        private PendingIntent _pendingIntent;
        private MediaSessionCompat.Token _sessionToken;
        private MediaSessionManager _sessionManager;
        private Context _appliactionContext;
        private NotificationCompat.Builder _builder;
        private bool _started = false;
        private string _currentMediaFileUrl;

        public MediaNotificationManagerImplementation(Context appliactionContext, MediaSessionManager sessionManager, Type serviceType)
        {
            if (SmallIconDrawableId == 0)
            {
                var icon = (appliactionContext.Resources?.GetIdentifier("xam_mediamanager_notify_ic", "drawable", appliactionContext?.PackageName)).GetValueOrDefault(0);
                SmallIconDrawableId = icon != 0 ? icon : appliactionContext.ApplicationInfo.Icon;
            }
            if (NextIconDrawableId == 0) NextIconDrawableId = Android.Resource.Drawable.IcMediaNext;
            if (PlayIconDrawableId == 0) PlayIconDrawableId = Android.Resource.Drawable.IcMediaPlay;
            if (PauseIconDrawableId == 0) PauseIconDrawableId = Android.Resource.Drawable.IcMediaPause;
            if (PreviousIconDrawableId == 0) PreviousIconDrawableId = Android.Resource.Drawable.IcMediaPrevious;


            _sessionManager = sessionManager;
            _sessionToken = _sessionManager.CurrentSession.SessionToken;
            _appliactionContext = appliactionContext;
            _intent = new Intent(_appliactionContext, serviceType);
            var mainActivity = _appliactionContext.PackageManager.GetLaunchIntentForPackage(_appliactionContext.PackageName);
            _pendingIntent = PendingIntent.GetActivity(_appliactionContext, 0, mainActivity, PendingIntentFlags.UpdateCurrent);

            StopNotifications();
        }

        /// <summary>
        /// Starts the notification.
        /// </summary>
        /// <param name="mediaFile">The media file.</param>
        public void StartNotification(IMediaFile mediaFile)
        {
            StartNotification(mediaFile, true, false);
        }

        /// <summary>
        /// When we start on the foreground we will present a notification to the user
        /// When they press the notification it will take them to the main page so they can control the music
        /// </summary>
        public void StartNotification(IMediaFile mediaFile, bool mediaIsPlaying, bool canBeRemoved)
        {
            if (mediaFile == null)
            {
                StopNotifications();
                return;
            }
            if (!_started || !string.Equals(_currentMediaFileUrl, mediaFile.Url))
            {
                Notification notification = CreateNotification(mediaFile,mediaIsPlaying);
                NotificationManagerCompat.From(_appliactionContext).Notify(MediaServiceBase.NotificationId, notification);
                UpdateMetadata(mediaFile);
                _started = true;
                _currentMediaFileUrl = mediaFile.Url;
            }

        }

        private Notification CreateNotification(IMediaFile mediaFile, bool mediaIsPlaying)
        {

            _builder = new NotificationCompat.Builder(_appliactionContext);

            _builder.AddAction(GenerateActionCompat(PreviousIconDrawableId, "Previous", MediaServiceBase.ActionPrevious));
            _builder.AddAction(mediaIsPlaying
                ? GenerateActionCompat(PauseIconDrawableId, "Pause", MediaServiceBase.ActionPause)
                : GenerateActionCompat(PlayIconDrawableId, "Play", MediaServiceBase.ActionPlay));
            _builder.AddAction(GenerateActionCompat(NextIconDrawableId, "Next", MediaServiceBase.ActionNext));

            _builder
                .SetStyle(new NotificationCompat.MediaStyle()
                    .SetShowActionsInCompactView(new int[] { 0, 1, 2 })  // show buttons in compact view
                    .SetMediaSession(_sessionToken)
                    .SetCancelButtonIntent(_pendingCancelIntent))
                .SetSmallIcon(SmallIconDrawableId)
                .SetVisibility(Android.Support.V4.App.NotificationCompat.VisibilityPublic)
                .SetContentIntent(_pendingIntent)
                .SetContentTitle(mediaFile?.Metadata?.Title ?? string.Empty)
                .SetContentText(mediaFile?.Metadata?.Artist ?? string.Empty)
                .SetContentInfo(mediaFile?.Metadata?.Album ?? string.Empty)
                .SetLargeIcon(mediaFile?.Metadata?.Art as Bitmap)
                .SetOngoing(mediaIsPlaying);

            return _builder.Build();
        }


        public void StopNotifications()
        {
            _started = false;
            _currentMediaFileUrl = null;
            NotificationManagerCompat nm = NotificationManagerCompat.From(_appliactionContext);
            nm.CancelAll();
        }

        private bool _updatingNotifications = false;
        private IMediaFile _lastUpdatingNotificationMediaFile = null;
        private MediaPlayerStatus _lastUpdatingNotificationMediaStatus;

        public async void UpdateNotifications(IMediaFile mediaFile, MediaPlayerStatus status)
        {
            // We have a problem if this is called too often
            // Workaround:
            _lastUpdatingNotificationMediaFile = mediaFile;
            _lastUpdatingNotificationMediaStatus = status;
            if (!_updatingNotifications)
            {
                _updatingNotifications = true;
                await Task.Run(async () => await Task.Delay(250));
                UpdateNotificationsDirect(_lastUpdatingNotificationMediaFile, _lastUpdatingNotificationMediaStatus);
                _updatingNotifications = false;
            }
            else
            {
                Debug.WriteLine($"Plugin.MediaManager: Prevented UpdateNotifications (Status = {status}), File={mediaFile?.Url}");
            }

        }

        private void UpdateNotificationsDirect(IMediaFile mediaFile, MediaPlayerStatus status)
        {
            Debug.WriteLine($"Plugin.MediaManager: UpdateNotifications (Status = {status}), File={mediaFile?.Url}");

            if (mediaFile == null)
            {
                StopNotifications();
            }
            else
            {
                try
                {
                    switch (status)
                    {
                        case MediaPlayerStatus.Stopped:
                        case MediaPlayerStatus.Failed:
                            StopNotifications();
                            break;
                        case MediaPlayerStatus.Buffering:
                        case MediaPlayerStatus.Playing:
                            Notification notification = CreateNotification(mediaFile, true);
                            NotificationManagerCompat.From(_appliactionContext).Notify(MediaServiceBase.NotificationId, notification);
                            _started = true;
                            _currentMediaFileUrl = mediaFile.Url;
                            break;
                        case MediaPlayerStatus.Paused:
                            Notification notification2 = CreateNotification(mediaFile, false);
                            NotificationManagerCompat.From(_appliactionContext).Notify(MediaServiceBase.NotificationId, notification2);
                            _started = true;
                            _currentMediaFileUrl = mediaFile.Url;
                            break;
                        case MediaPlayerStatus.Loading:
                            break;

                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    StopNotifications();
                }
            }
            if (mediaFile != _lastUpdatingNotificationMediaFile || status != _lastUpdatingNotificationMediaStatus)
            {
                Debug.WriteLine($"Plugin.MediaManager: Notification not up to date: (Status = {_lastUpdatingNotificationMediaStatus}), File={_lastUpdatingNotificationMediaFile?.Url}");
                UpdateNotifications(_lastUpdatingNotificationMediaFile, _lastUpdatingNotificationMediaStatus);
            }
        }

        /// <summary>
        /// Updates the metadata on the lock screen
        /// </summary>
        /// <param name="currentTrack"></param>
        private void UpdateMetadata(IMediaFile currentTrack)
        {

            MediaMetadataCompat.Builder builder = new MediaMetadataCompat.Builder();

            if (currentTrack?.Metadata != null && _sessionManager?.CurrentSession != null)
            {
                builder
                    .PutString(MediaMetadata.MetadataKeyAlbum, currentTrack.Metadata.Artist)
                    .PutString(MediaMetadata.MetadataKeyArtist, currentTrack.Metadata.Artist)
                    .PutString(MediaMetadata.MetadataKeyTitle, currentTrack.Metadata.Title);
                if (ShowFullscreenAlbumArtInLockscreen)
                {
                    builder.PutBitmap(MediaMetadata.MetadataKeyAlbumArt, currentTrack.Metadata.AlbumArt as Bitmap);
                }
                _sessionManager.CurrentSession.SetMetadata(builder.Build());
            }
           
        }


        private Android.Support.V4.App.NotificationCompat.Action GenerateActionCompat(int icon, string title, string intentAction)
        {
            _intent.SetAction(intentAction);

            PendingIntentFlags flags = PendingIntentFlags.UpdateCurrent;
            if (intentAction.Equals(MediaServiceBase.ActionStop))
                flags = PendingIntentFlags.CancelCurrent;

            PendingIntent pendingIntent = PendingIntent.GetService(_appliactionContext, 1, _intent, flags);

            return new Android.Support.V4.App.NotificationCompat.Action.Builder(icon, title, pendingIntent).Build();
        }


    }
}