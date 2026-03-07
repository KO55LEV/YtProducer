cd src/YtProducer.Api
dotnet run



cd ui
npm install
npm run dev



dotnet run --project src/YtProducer.Console -- playlist_init

// get playlists
dotnet run   -- playlists

// create folder 
dotnet run   -- playlist_init
// playlist status becomes FolderCreated
dotnet run   -- playlist_init <playlistId>

// generated loop assets are written under YT_PRODUCER_LOOP_WORKING_DIRECTORY/<playlistId>/<loopId>/

// schedule visualizer renders from existing playlist folders
// requires YT_PRODUCER_MCP_MEDIA_WORKING_DIRECTORY and YT_PRODUCER_MCP_MEDIA_PROJECT in .env
// optional: YT_PRODUCER_MEDIA_PARALLELISM (default 3)
dotnet run   -- generate-media
dotnet run   -- generate-media <playlistId>

// generate image for a specific playlist position (optional model override)
// requires YT_PRODUCER_MCP_KIEAI_WORKING_DIRECTORY and YT_PRODUCER_MCP_KIEAI_PROJECT in .env
dotnet run   -- generate-image <playlistId> <position> [model]

// generate music for a specific playlist position using track.metadata.musicGenerationPrompt
// outputs files as <position>.mp3, <position>_2.mp3, ...
dotnet run   -- generate-music <playlistId> <position>

// generate images for all tracks in playlist using model grok-imagine/text-to-image
// on full success playlist status becomes ImagesGenerated
dotnet run   -- generate-all-images <playlistId>

// generate music for all tracks in playlist
// on full success playlist status becomes MusicGenerated
dotnet run   -- generate-all-music <playlistId>

// create a YouTube playlist from local playlist data
// requires YT_PRODUCER_MCP_YOUTUBE_WORKING_DIRECTORY and YT_PRODUCER_MCP_YOUTUBE_PROJECT in .env
dotnet run   -- generate-youtube-playlist <playlistId>

// upload a playlist video for a specific position
dotnet run   -- upload-youtube-video <playlistId> <position>

// upload a YouTube thumbnail for a specific position
// prefers <position>_thumbnail.*; fallback to first <position> image
dotnet run   -- upload-youtube-thumbnail <playlistId> <position>

// create branded track thumbnail from first track image
// output file: <position>_thumbnail.<originalExt> in the same playlist folder
// optional env overrides: YT_PRODUCER_THUMBNAIL_LOGO_PATH, YT_PRODUCER_THUMBNAIL_HEADLINE, YT_PRODUCER_THUMBNAIL_SUBHEADLINE
dotnet run   -- track-create-youtube-video-thumbnail <playlistId> [position]

// add all uploaded track videos (track_on_youtube) to playlist in one MCP call
dotnet run   -- add-youtube-videos-to-playlist <playlistId>

dotnet run --project src/YtProducer.Console -- generate-image ea15b70c-dd1a-48c9-8514-6d2f3dce4811 5
dotnet run --project src/YtProducer.Console -- generate-image ea15b70c-dd1a-48c9-8514-6d2f3dce4811 5 grok-imagine/text-to-image
dotnet run -- generate-image ea15b70c-dd1a-48c9-8514-6d2f3dce4811 5
dotnet run -- generate-image ea15b70c-dd1a-48c9-8514-6d2f3dce4811 9 grok-imagine/text-to-image
dotnet run -- generate-music ea15b70c-dd1a-48c9-8514-6d2f3dce4811 5
 
// create folder 
dotnet run   -- generate-media 



dotnet run --project src/YtProducer.Console -- generate-youtube-playlist <playlistId>

//create channel 
dotnet run -- generate-youtube-playlist ea15b70c-dd1a-48c9-8514-6d2f3dce4811



dotnet run --project src/YtProducer.Console -- generate-youtube-playlist <playlistId> [privacy] private unlisted public

//create channel 
dotnet run -- generate-youtube-playlist ea15b70c-dd1a-48c9-8514-6d2f3dce4811 public

dotnet run -- upload-youtube-video <playlistId> <position> 
// description is auto-generated from track fields + metadata
// optional env customization: YT_PRODUCER_BRAND_NAME, YT_PRODUCER_YOUTUBE_SUBSCRIBE_LINK, YT_PRODUCER_YOUTUBE_PLAYLIST_LINK
// publish scheduling uses DB singleton table youtube_last_published_date and slots: 07:30, 11:00, 14:30, 18:00, 21:30 UTC

dotnet run -- upload-youtube-video ea15b70c-dd1a-48c9-8514-6d2f3dce4811 1

// upload video and then upload thumbnail for same track position
dotnet run -- upload-youtube-video-with-thumbnail <playlistId> <position>
dotnet run -- upload-youtube-video-with-thumbnail ea15b70c-dd1a-48c9-8514-6d2f3dce4811 1

// upload all track videos + thumbnails for playlist (sequential)
// on full success playlist status becomes OnYoutube
dotnet run -- upload-youtube-videos <playlistId>


dotnet run -- upload-youtube-thumbnail <playlistId> <position> 
dotnet run -- upload-youtube-thumbnail ea15b70c-dd1a-48c9-8514-6d2f3dce4811 1

dotnet run -- track-create-youtube-video-thumbnail <playlistId> [position]
dotnet run -- track-create-youtube-video-thumbnail ea15b70c-dd1a-48c9-8514-6d2f3dce4811 1
dotnet run -- track-create-youtube-video-thumbnail ea15b70c-dd1a-48c9-8514-6d2f3dce4811



 

dotnet run -- add-youtube-video-to-playlist <playlistId> 
dotnet run -- add-youtube-video-to-playlist ea15b70c-dd1a-48c9-8514-6d2f3dce4811

dotnet run   -- add-youtube-videos-to-playlist ea15b70c-dd1a-48c9-8514-6d2f3dce4811


dotnet run -- add-youtube-video-to-playlist <playlistId> 
dotnet run -- add-youtube-video-to-playlist ea15b70c-dd1a-48c9-8514-6d2f3dce4811

dotnet run   -- add-youtube-videos-to-playlist ea15b70c-dd1a-48c9-8514-6d2f3dce4811


dotnet run -- track-create-youtube-video-thumbnail <playlistId> <position>

dotnet run -- track-create-youtube-video-thumbnail ea15b70c-dd1a-48c9-8514-6d2f3dce4811 1
