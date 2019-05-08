using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading;
using System.Web;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.Framework.Blobs;
using EPiServer.Logging;
using EPiServer.PlugIn;
using EPiServer.Scheduler;
using EPiServer.Security;
using EPiServer.ServiceLocation;


namespace Web.Business.Initialization
{
    [ScheduledPlugIn(DisplayName = "OptimizeBlobsImages",
        Description = "Looks up all the images on the site and compresses them to an optimal state. After the job has ran, please run \"Clear Thumbnail Properties\" to force EpiServer to refresh the images.",
        IntervalLength = 300000, IntervalType = ScheduledIntervalType.Minutes)]
    public class OptimizeBlobsImages : ScheduledJobBase
    {
        private static ILogger _log = LogManager.Instance.GetLogger("RewardImportLog");
        private bool _stopSignaled;
        private static readonly object TaskLock = new object();
        private IBlobFactory _blobResolver;
        private readonly IContentLoader _readRepo = ServiceLocator.Current.GetInstance<IContentLoader>();
        private readonly IContentRepository _writeRepo = ServiceLocator.Current.GetInstance<IContentRepository>();

        private readonly ImageCompressor _imageCompressor;

        public OptimizeBlobsImages()
        {
            var parseStatus = int.TryParse(ConfigurationManager.AppSettings["QualityLevel"], out int compressionLevel);
             _imageCompressor = new ImageCompressor(parseStatus ? compressionLevel : 75);
            IsStoppable = true;
        }

        public override void Stop()
        {
            _stopSignaled = true;
        }

        public override string Execute()
        {
            if (!Monitor.TryEnter(TaskLock))
            {
                return "Job is already running.";
            }

            try
            {
                #region Set Principal Admin...
                if (HttpContext.Current == null)
                {
                    PrincipalInfo.CurrentPrincipal = new GenericPrincipal(new GenericIdentity("Import/Update Rewards"),
                        new[]
                        {
                            "knowitadmin"
                        });
                }

                if (PrincipalInfo.CurrentPrincipal.Identity.Name == string.Empty)
                {
                    _log.Log(Level.Error, "OBS! ===> Anonymous execution! ;(");
                    return "OBS! ===> Anonymous execution!";

                }
                #endregion

                GetAllBlobs();
            }
            catch (Exception exc)
            {
                _log.Error(exc.Message);
                _log.Error(exc.StackTrace);

                return string.Format("Unknown exception caught ({0} {1})",
                    new object[] { exc.Message, exc.StackTrace });
            }
            finally
            {
                Monitor.Exit(TaskLock);
            }

            if (_stopSignaled)
            {
                return "Stop of job was called";
            }

            return $"Success: Optimized {ImageCompressor.OptimizedImagesCount} images (Saved: {ImageCompressor.TotallySavedMbs} mb)." +
                   $"\nSkipped {ImageCompressor.SkippedImagesCount} images.";
        }

        public void GetAllBlobs()
        {
            _blobResolver = ServiceLocator.Current.GetInstance<IBlobFactory>();
            var globalAssetsRoot = EPiServer.Web.SiteDefinition.Current.GlobalAssetsRoot;

            var mediaChildRefs = _readRepo.GetDescendents(globalAssetsRoot);

            foreach (var child in mediaChildRefs)
            {
                var image = _readRepo.TryGet<ImageData>(child, out var imageData);
                if (!image) continue;

                var blobLink = UrlHelper.GetExternalUrlNoHttpContext(imageData.ContentLink);
                if (!RemoteFileExists(blobLink))
                {
                    continue;
                }

                CompressBlob(imageData);
                
            }
            _log.Error(
                $"Success: Optimized {ImageCompressor.OptimizedImagesCount} images (Saved: {ImageCompressor.TotallySavedMbs} mb)." +
                $"\nSkipped {ImageCompressor.SkippedImagesCount} images.");
        }

        public void CompressBlob(ImageData image)
        {
            byte[] originalImage;
            using (var memoryStream = new MemoryStream())
            {
                image.BinaryData.OpenRead().CopyTo(memoryStream);
                originalImage = memoryStream.ToArray();
            }

            byte[] compressedResult = {};
            switch (image.MimeType)
            {
                case "image/jpeg":
                    compressedResult = _imageCompressor.MakeCompressedJpg(originalImage);
                    break;

                case "image/png":
                    compressedResult = _imageCompressor.MakeCompressedPng(originalImage);
                    break;

                default:
                    _log.Error("!unknown file:" + image.MimeType);
                    break;

            }

            if (ImageCompressor.OptimizedImagesCount % 50 == 0 && ImageCompressor.OptimizedImagesCount !=0)
            {
                OnStatusChanged($"Updated  {ImageCompressor.OptimizedImagesCount}...");
            }

            if (compressedResult == null) { return; }

            //Create the Compressed Blob.
            var writableImage = image.CreateWritableClone() as ImageFile;
            if (writableImage != null)
            {
                var blob = _blobResolver.CreateBlob(writableImage.BinaryDataContainer, Path.GetExtension(image.Name));
                using (var s = blob.OpenWrite())
                {
                    var b = new BinaryWriter(s);
                    b.Write(compressedResult);
                    b.Flush();
                }

                writableImage.BinaryData = blob;
                _writeRepo.Save(writableImage, SaveAction.Publish, AccessLevel.NoAccess);

            }


        }
        private bool RemoteFileExists(string url)
        {
            try
            {
                HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
                if (request != null)
                {
                    request.Method = "HEAD";
                    HttpWebResponse response = request.GetResponse() as HttpWebResponse;
                    if (response != null)
                    {
                        response.Close();
                        return (response.StatusCode == HttpStatusCode.OK);
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
        
    }


}