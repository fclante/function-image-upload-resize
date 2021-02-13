// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}

// Learn how to locally debug an Event Grid-triggered function:
//    https://aka.ms/AA30pjh

// Use for local testing:
//   https://{ID}.ngrok.io/runtime/webhooks/EventGrid?functionName=Thumbnail

using Azure.Storage.Blobs;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace ImageFunctions
{
    public class Thumbnail
    {
        private readonly BlobStorageConfig _config;

        public Thumbnail(IOptions<BlobStorageConfig> config)
        {
            _config = config.Value;
        }

        private static string GetBlobNameFromUrl(string blobUrl)
        {
            var uri = new Uri(blobUrl);
            var blobClient = new BlobClient(uri);
            return blobClient.Name;
        }

        private static IImageEncoder GetEncoder(string extension)
        {
            IImageEncoder encoder = null;

            extension = extension.Replace(".", "");

            var isSupported = Regex.IsMatch(extension, "gif|png|jpe?g", RegexOptions.IgnoreCase);

            if (isSupported)
            {
                switch (extension.ToLower())
                {
                    case "png":
                        encoder = new PngEncoder();
                        break;
                    case "jpg":
                        encoder = new JpegEncoder();
                        break;
                    case "jpeg":
                        encoder = new JpegEncoder();
                        break;
                    case "gif":
                        encoder = new GifEncoder();
                        break;
                    default:
                        break;
                }
            }

            return encoder;
        }

        [FunctionName("Thumbnail")]
        public async Task Run(
            [EventGridTrigger] EventGridEvent eventGridEvent,
            [Blob("{data.url}", FileAccess.Read)] Stream input,
            ILogger log)
        {
            try
            {
                if (input != null)
                {
                    var createdEvent = ((JObject) eventGridEvent.Data).ToObject<StorageBlobCreatedEventData>();
                    var createdEventUrl = createdEvent.Url;
                    var extension = Path.GetExtension(createdEventUrl);
                    var encoder = GetEncoder(extension);

                    if (encoder != null)
                    {
                        var thumbnailWidth = Convert.ToInt32(_config.ThumbnailWidth);
                        var thumbContainerName = _config.ThumbnailContainer;
                        var blobServiceClient = new BlobServiceClient(_config.BlobStorageConnectionString);
                        var blobContainerClient = blobServiceClient.GetBlobContainerClient(thumbContainerName);
                        var blobName = GetBlobNameFromUrl(createdEventUrl);

                        using (var output = new MemoryStream())
                        using (Image<Rgba32> image = Image.Load(input))
                        {
                            var divisor = image.Width / thumbnailWidth;
                            var imageHeight = (image.Height / divisor);
                            var height = Convert.ToInt32(Math.Round((decimal)imageHeight));

                            image.Mutate(x => x.Resize(thumbnailWidth, height));
                            image.Save(output, encoder);
                            output.Position = 0;
                            await blobContainerClient.UploadBlobAsync(blobName, output);
                        }
                    }
                    else
                    {
                        log.LogInformation("No encoder support for: {CreatedEventUrl}", createdEventUrl);
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogInformation(ex.Message);
                throw;
            }
        }
    }
}