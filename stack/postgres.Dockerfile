FROM postgres:17-alpine

COPY stack/postgres-shutdown-backup.sh /usr/local/bin/postgres-shutdown-backup.sh

RUN chmod 0755 /usr/local/bin/postgres-shutdown-backup.sh

ENTRYPOINT ["/usr/local/bin/postgres-shutdown-backup.sh"]
CMD ["postgres"]
