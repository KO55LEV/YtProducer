pkill -f "YtProducer.Console process_all_jobs"
pkill -f "dotnet run -- process_all_jobs"


psql "host=localhost port=5432 dbname=YtProducer user=myuser password=mypass" <<'SQL'
begin;

delete from track_on_youtube
where playlist_id = '<PLAYLIST_ID>';

update jobs
set status = 'Pending',
    worker_id = null,
    progress = 0,
    started_at = null,
    finished_at = null,
    last_heartbeat = now() at time zone 'utc',
    lease_expires_at = null,
    error_code = null,
    error_message = null,
    result_json = null,
    retry_count = 0
where target_id = '<PLAYLIST_ID>'
  and type = 'UploadYoutubeVideos';

commit;
SQL
