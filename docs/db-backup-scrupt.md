
docker ps


docker exec -e PGPASSWORD=mypass <postgres_container> pg_dump -U myuser -d YtProducer -Fc -f /tmp/ytproducer.dump && docker cp <postgres_container>:/tmp/ytproducer.dump ./ytproducer.dump


docker exec -e PGPASSWORD=mypass 4bf60ac51fd6 pg_dump -U myuser -d YtProducer -Fc -f /tmp/ytproducer.dump && docker cp 4bf60ac51fd6:/tmp/ytproducer.dump ./ytproducer.dump
