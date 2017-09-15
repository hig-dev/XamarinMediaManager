using System;
using System.Runtime.InteropServices;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
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
    public class MediaNotificationManagerImplementation : MediaControllerCompat.Callback, IMediaNotificationManager
    {
        private readonly MediaSessionManager _mediaSession;
        private MediaSessionCompat.Token _sessionToken;
        private readonly int _notificationColor;
        private readonly NotificationManagerCompat _notificationManager;
        private PlaybackStateCompat _playbackState;
        private MediaMetadataCompat _metadata;
        private bool _started = false;
        private const int NotificationId = 412;
        private const int RequestCode = 100;
        private readonly PendingIntent _pauseIntent;
        private readonly PendingIntent _playIntent;
        private readonly PendingIntent _previousIntent;
        private readonly PendingIntent _nextIntent;
        private readonly BroadcastReceiver _notificationBroadcastReceiver;
        private IMediaFile _mediaFile;

        public static int PreviousIconDrawableId = 0;
        public static int NextIconDrawableId = 0;
        public static int PlayIconDrawableId = 0;
        public static int PauseIconDrawableId = 0;
        public static int SmallIconDrawableId = 0;


        public MediaNotificationManagerImplementation(MediaSessionManager mediaSession, Context context)
        {
            _mediaSession = mediaSession;
            if (SmallIconDrawableId == 0)
            {
                var icon = (context.Resources?.GetIdentifier("xam_mediamanager_notify_ic", "drawable", context?.PackageName)).GetValueOrDefault(0);
                SmallIconDrawableId = icon != 0 ? icon : context.ApplicationInfo.Icon;
            }
            if (NextIconDrawableId == 0) NextIconDrawableId = Android.Resource.Drawable.IcMediaNext;
            if (PlayIconDrawableId == 0) PlayIconDrawableId = Android.Resource.Drawable.IcMediaPlay;
            if (PauseIconDrawableId == 0) PauseIconDrawableId = Android.Resource.Drawable.IcMediaPause;
            if (PreviousIconDrawableId == 0) PreviousIconDrawableId = Android.Resource.Drawable.IcMediaPrevious;

            _notificationBroadcastReceiver = new NotificationBroadcastReceiver(mediaSession);
            UpdateSessionToken(context);
            _notificationColor = GetThemeColor(context, Resource.Attribute.ColorPrimary, Color.DarkGray);
            _notificationManager = NotificationManagerCompat.From(context);
            string pkg = context.PackageName;
            _pauseIntent = PendingIntent.GetBroadcast(context, RequestCode, new Intent(MediaServiceBase.ActionPause).SetPackage(pkg), PendingIntentFlags.CancelCurrent);
            _playIntent = PendingIntent.GetBroadcast(context, RequestCode,new Intent(MediaServiceBase.ActionPlay).SetPackage(pkg), PendingIntentFlags.CancelCurrent);
            _previousIntent = PendingIntent.GetBroadcast(context, RequestCode, new Intent(MediaServiceBase.ActionPrevious).SetPackage(pkg), PendingIntentFlags.CancelCurrent);
            _nextIntent = PendingIntent.GetBroadcast(context, RequestCode, new Intent(MediaServiceBase.ActionNext).SetPackage(pkg), PendingIntentFlags.CancelCurrent);

            // Cancel all notifications to handle the case where the Service was killed and
            // restarted by the system.
            _notificationManager.CancelAll();

        }
        private static int GetThemeColor(Context context, int attribute, int defaultColor)
        {
            int themeColor = 0;
            string packageName = context.PackageName;
            try
            {
                Context packageContext = context.CreatePackageContext(packageName, 0);
                ApplicationInfo applicationInfo = context.PackageManager.GetApplicationInfo(packageName, 0);
                packageContext.SetTheme(applicationInfo.Theme);
                Resources.Theme theme = packageContext.Theme;
                TypedArray ta = theme.ObtainStyledAttributes(new int[] { attribute });
                themeColor = ta.GetColor(0, defaultColor);
                ta.Recycle();
            }
            catch (PackageManager.NameNotFoundException e)
            {
                e.PrintStackTrace();
            }
            return themeColor;
        }
        internal void UpdateSessionToken(Context context)
        {
            if (_mediaSession.CurrentSession != null)
            {
                MediaSessionCompat.Token freshToken = _mediaSession.CurrentSession.SessionToken;
                if (_sessionToken == null && freshToken != null || _sessionToken != null && !_sessionToken.Equals(freshToken))
                {
                    _mediaSession?.CurrentSession?.Controller?.UnregisterCallback(this);
                    _sessionToken = freshToken;
                    if (_sessionToken != null)
                    {
                        _mediaSession.MediaController = new MediaControllerCompat(context, _sessionToken);
                        if (_started)
                        {
                            _mediaSession?.CurrentSession?.Controller?.RegisterCallback(this);
                        }
                    }
                }
            }
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

            _mediaFile = mediaFile;
            if (!_started)
            {
                _mediaSession.UpdateMetadata(mediaFile);
                _mediaSession.UpdatePlaybackState(mediaIsPlaying
                    ? PlaybackStateCompat.StatePlaying
                    : PlaybackStateCompat.StatePaused);
                _metadata = _mediaSession.MediaController.Metadata;
                _playbackState = _mediaSession.MediaController.PlaybackState;

                // The notification must be updated after setting started to true
                Notification notification = CreateNotification(mediaFile);
                if (notification != null)
                {
                    _mediaSession.MediaController.RegisterCallback(this);
                    IntentFilter filter = new IntentFilter();
                    filter.AddAction(MediaServiceBase.ActionNext);
                    filter.AddAction(MediaServiceBase.ActionPause);
                    filter.AddAction(MediaServiceBase.ActionPlay);
                    filter.AddAction(MediaServiceBase.ActionPrevious);
                    _mediaSession.MediaService.RegisterReceiver(_notificationBroadcastReceiver, filter);
                    _mediaSession.MediaService.StartForeground(NotificationId, notification);
                    _started = true;
                }
            }
        }

        private Notification CreateNotification(IMediaFile mediaFile)
        {
            _mediaFile = mediaFile;
            Console.WriteLine("Plugin.MediaManager: updateNotificationMetadata. Metadata=" + _metadata);
            if (_metadata == null || _playbackState == null)
            {
                return null;
            }

            NotificationCompat.Builder notificationBuilder = new NotificationCompat.Builder(_mediaSession.MediaService);

            notificationBuilder.AddAction(PreviousIconDrawableId, "Previous", _previousIntent);
            AddPlayPauseAction(notificationBuilder);
            notificationBuilder.AddAction(NextIconDrawableId, "Next", _nextIntent);


            MediaDescriptionCompat description = _metadata.Description;

            Bitmap art = mediaFile?.Metadata?.AlbumArt as Bitmap;

            notificationBuilder
                    .SetStyle(new NotificationCompat.MediaStyle()
                    .SetShowActionsInCompactView(new int[] { 0,1,2 })  // show buttons in compact view
                    .SetMediaSession(_sessionToken))
                    .SetColor(_notificationColor)
                    .SetSmallIcon(SmallIconDrawableId)
                    .SetVisibility(Android.Support.V4.App.NotificationCompat.VisibilityPublic)
                    .SetUsesChronometer(true)
                    .SetContentIntent(CreateContentIntent(description))
                    .SetContentTitle(description.Title)
                    .SetContentText(description.Subtitle)
                    .SetLargeIcon(art);


            SetNotificationPlaybackState(notificationBuilder);
           
            return notificationBuilder.Build();
        }
        private PendingIntent CreateContentIntent(MediaDescriptionCompat description)
        {
            Intent openUI = _mediaSession.ApplicationContext.PackageManager.GetLaunchIntentForPackage(_mediaSession.ApplicationContext.PackageName);
            openUI.SetFlags(ActivityFlags.SingleTop);
            return PendingIntent.GetActivity(_mediaSession.MediaService, RequestCode, openUI, PendingIntentFlags.CancelCurrent);
        }
        private static readonly DateTime Jan1St1970 = new DateTime (1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public static long CurrentTimeMillis()
        {
            return (long)(DateTime.UtcNow - Jan1St1970).TotalMilliseconds;
        }
        private void SetNotificationPlaybackState(NotificationCompat.Builder builder)
        {
            Console.WriteLine("updateNotificationPlaybackState. mPlaybackState=" + _playbackState);
            if (_playbackState == null || !_started)
            {
                Console.WriteLine("updateNotificationPlaybackState. cancelling notification!");
                _mediaSession.MediaService.StopForeground(true);
                return;
            }
            if (_playbackState.State == PlaybackStateCompat.StatePlaying && _playbackState.Position >= 0)
            {
                Console.WriteLine("updateNotificationPlaybackState. updating playback");
                builder
                    .SetWhen(CurrentTimeMillis() - _playbackState.Position)
                    .SetShowWhen(true)
                    .SetUsesChronometer(true);
            }
            else
            {
                Console.WriteLine("updateNotificationPlaybackState. hiding playback position");
                builder
                    .SetWhen(0)
                    .SetShowWhen(false)
                    .SetUsesChronometer(false);
            }

            // Make sure that the notification can be dismissed by the user when we are not playing:
            builder.SetOngoing(_playbackState.State == PlaybackStateCompat.StatePlaying);
        }

        private void AddPlayPauseAction(NotificationCompat.Builder builder)
        {
            Console.WriteLine("Plugin.MediaManager: Update Play/Pause");
            String label;
            int icon;
            PendingIntent intent;
            if (_playbackState.State == PlaybackStateCompat.StatePlaying)
            {
                label ="Pause";
                icon = PauseIconDrawableId;
                intent = _pauseIntent;
            }
            else
            {
                label = "Play";
                icon = PlayIconDrawableId;
                intent = _playIntent;
            }
            builder.AddAction(new NotificationCompat.Action(icon, label, intent));
        }
        public void StopNotifications()
        {
            //NotificationManagerCompat nm = NotificationManagerCompat.From(_applicationContext);
            //nm.CancelAll();


            if (_started)
            {
                _started = false;
                _mediaSession.MediaController.UnregisterCallback(this);
                try
                {
                    _notificationManager.Cancel(NotificationId);
                    _mediaSession.MediaService.UnregisterReceiver(_notificationBroadcastReceiver);
                }
                catch (Exception ex)
                {
                    // ignore if the receiver is not registered.
                }
                _mediaSession.MediaService.StopForeground(true);
            }
        }

        public override void OnMetadataChanged(MediaMetadataCompat metadata)
        {
            _metadata = metadata;
            Console.WriteLine("Received new metadata " + metadata);
            Notification notification = CreateNotification(_mediaFile);
            if (notification != null)
            {
                _notificationManager.Notify(NotificationId, notification);
            }
        }

        public override void OnPlaybackStateChanged(PlaybackStateCompat state)
        {
            _playbackState = state;
            Console.WriteLine("Plugin.MediaManager: Received new playback state" + state);
            if (state.State == PlaybackStateCompat.StateStopped ||
                state.State == PlaybackStateCompat.StateNone)
            {
                StopNotifications();
            }
            else
            {
                Notification notification = CreateNotification(_mediaFile);
                if (notification != null)
                {
                    _notificationManager.Notify(NotificationId, notification);
                }
            }
        }

        public override void OnSessionDestroyed()
        {
            base.OnSessionDestroyed();
            Console.WriteLine("Plugin.MediaManager: Session was destroyed, resetting to the new session token");
            try
            {
                UpdateSessionToken(_mediaSession.ApplicationContext);
            }
            catch (RemoteException e)
            {
                Console.WriteLine("Plugin.MediaManager: could not connect media controller");
            }
        }

        public void UpdateNotifications(IMediaFile mediaFile, MediaPlayerStatus status)
        {
            _mediaFile = mediaFile;
        }

    }

    public class NotificationBroadcastReceiver : BroadcastReceiver
    {
        public MediaSessionManager MediaSessionManager { get; set; }
        public override void OnReceive(Context context, Intent intent)
        {
            if (intent?.Action == null || MediaSessionManager == null)
                return;

            MediaSessionManager?.HandleAction(intent?.Action);
        }

        public NotificationBroadcastReceiver(MediaSessionManager mediaSessionManager)
        {
            MediaSessionManager = mediaSessionManager;
        }

        public NotificationBroadcastReceiver(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }
    }


}