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

// schedule visualizer renders from existing playlist folders
// requires YT_PRODUCER_MCP_MEDIA_WORKING_DIRECTORY and YT_PRODUCER_MCP_MEDIA_PROJECT in .env
// optional: YT_PRODUCER_MEDIA_PARALLELISM (default 3)
dotnet run   -- generate-media

// generate image for a specific playlist position (optional model override)
// requires YT_PRODUCER_MCP_KIEAI_WORKING_DIRECTORY and YT_PRODUCER_MCP_KIEAI_PROJECT in .env
dotnet run   -- generate-image <playlistId> <position> [model]

// create a YouTube playlist from local playlist data
// requires YT_PRODUCER_MCP_YOUTUBE_WORKING_DIRECTORY and YT_PRODUCER_MCP_YOUTUBE_PROJECT in .env
dotnet run   -- generate-youtube-playlist <playlistId>

// upload a playlist video for a specific position
dotnet run   -- upload-youtube-video <playlistId> <position>

// upload a YouTube thumbnail for a specific position (uses first image)
dotnet run   -- upload-youtube-thumbnail <playlistId> <position>

// add all uploaded track videos (track_on_youtube) to playlist in one MCP call
dotnet run   -- add-youtube-videos-to-playlist <playlistId>

dotnet run --project src/YtProducer.Console -- generate-image ea15b70c-dd1a-48c9-8514-6d2f3dce4811 5
dotnet run --project src/YtProducer.Console -- generate-image ea15b70c-dd1a-48c9-8514-6d2f3dce4811 5 grok-imagine/text-to-image
dotnet run -- generate-image ea15b70c-dd1a-48c9-8514-6d2f3dce4811 5
dotnet run -- generate-image ea15b70c-dd1a-48c9-8514-6d2f3dce4811 9 grok-imagine/text-to-image
 
// create folder 
dotnet run   -- generate-media 



dotnet run --project src/YtProducer.Console -- generate-youtube-playlist <playlistId>

//create channel 
dotnet run -- generate-youtube-playlist ea15b70c-dd1a-48c9-8514-6d2f3dce4811



dotnet run --project src/YtProducer.Console -- generate-youtube-playlist <playlistId> [privacy] private unlisted public

//create channel 
dotnet run -- generate-youtube-playlist ea15b70c-dd1a-48c9-8514-6d2f3dce4811 public

dotnet run -- upload-youtube-video <playlistId> <position> 

dotnet run -- upload-youtube-video ea15b70c-dd1a-48c9-8514-6d2f3dce4811 1


dotnet run -- upload-youtube-thumbnail <playlistId> <position> 
dotnet run -- upload-youtube-thumbnail ea15b70c-dd1a-48c9-8514-6d2f3dce4811 1



 

dotnet run -- add-youtube-video-to-playlist <playlistId> 
dotnet run -- add-youtube-video-to-playlist ea15b70c-dd1a-48c9-8514-6d2f3dce4811

dotnet run   -- add-youtube-videos-to-playlist ea15b70c-dd1a-48c9-8514-6d2f3dce4811
