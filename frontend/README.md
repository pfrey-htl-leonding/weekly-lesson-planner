# Weekly Lesson Planner frontend

Angular 22 standalone application using Angular Material and CDK.

## Local commands

Requires a Node version supported by Angular 22 (the stack build uses Node 24).

```bash
npm ci
npm start
npm test
npm run build
```

`npm start` proxies `/api` and `/health` to the backend at `http://localhost:5080`.
Production traffic is served and proxied by the Nginx container defined under `../stack`.
