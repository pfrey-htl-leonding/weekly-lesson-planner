# Repository agent instructions

## Attention notification

When work is blocked waiting for user input or command approval, attract the user's attention by running this command three times in succession:

```bash
aplay /usr/share/sounds/purple/receive.wav
aplay /usr/share/sounds/purple/receive.wav
aplay /usr/share/sounds/purple/receive.wav
```

Do not play the notification for routine progress updates or after work has already completed.
