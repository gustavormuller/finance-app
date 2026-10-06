import { mergeConfig, type Plugin } from 'vite';

import app from '../vite.config.ts';

/** The app with one module changed: the deploy the running worker must hand over to. */
const changed: Plugin = {
  name: 'e2e-second-build',
  transform(code, id) {
    return id.endsWith('/src/main.tsx') ? `${code}\ndocument.documentElement.dataset.build = 'second';\n` : null;
  },
};

export default mergeConfig(app, { plugins: [changed] });
