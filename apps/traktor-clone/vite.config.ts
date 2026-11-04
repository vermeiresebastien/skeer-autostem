import { defineConfig } from 'vite';
import { GoogleGenAI } from '@google/genai';

export default defineConfig({
  server: {
    port: 3000,
    host: '0.0.0.0',
  },
  plugins: [
    {
      name: 'gemini-server-endpoint',
      configureServer(server) {
        server.middlewares.use('/api/gemini/chat', async (req, res) => {
          if (req.method !== 'POST') {
            res.statusCode = 405;
            res.end(JSON.stringify({ error: 'Method Not Allowed' }));
            return;
          }

          let body = '';
          req.on('data', (chunk) => {
            body += chunk;
          });

          req.on('end', async () => {
            res.setHeader('Content-Type', 'application/json');
            try {
              const { messages, systemInstruction, model } = JSON.parse(body || '{}');

              const apiKey = process.env.GEMINI_API_KEY;
              if (!apiKey) {
                res.statusCode = 500;
                res.end(
                  JSON.stringify({
                    error:
                      'GEMINI_API_KEY is not configured in process.env. Please make sure the Gemini API key is attached.',
                  })
                );
                return;
              }

              const ai = new GoogleGenAI({
                apiKey,
                httpOptions: {
                  headers: {
                    'User-Agent': 'aistudio-build',
                  },
                },
              });

              // Format messages for @google/genai
              const contents = (messages || []).map((m: any) => ({
                role: m.role === 'user' ? 'user' : 'model',
                parts: [{ text: m.content || m.text || '' }],
              }));

              const selectedModel = model || 'gemini-3.8-flash';

              const response = await ai.models.generateContent({
                model: selectedModel,
                contents,
                config: {
                  systemInstruction: systemInstruction || undefined,
                },
              });

              res.statusCode = 200;
              res.end(JSON.stringify({ text: response.text || '' }));
            } catch (err: any) {
              console.error('Gemini API Error in Vite server:', err);
              res.statusCode = 500;
              res.end(
                JSON.stringify({
                  error: err?.message || 'Failed to generate response from Gemini API',
                })
              );
            }
          });
        });
      },
    },
  ],
});
