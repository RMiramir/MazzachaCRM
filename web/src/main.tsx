import '@mantine/core/styles.css';
import '@mantine/dates/styles.css';
import '@mantine/charts/styles.css';
import '@mantine/notifications/styles.css';
import './styles.css';

import { Badge, createTheme, MantineProvider, type MantineColorsTuple } from '@mantine/core';
import { DatesProvider } from '@mantine/dates';
import { Notifications } from '@mantine/notifications';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { App } from './App';
import { ConfirmProvider } from './components/ui';
import './lib/format';

const berry: MantineColorsTuple = [
  '#fff0f4',
  '#fbdde5',
  '#f3b8c8',
  '#ec91aa',
  '#e66f90',
  '#e35a80',
  '#d94a73',
  '#c93f66',
  '#b4365a',
  '#9e2a4d',
];

const theme = createTheme({
  primaryColor: 'berry',
  primaryShade: 7,
  colors: { berry },
  defaultRadius: 'md',
  cursorType: 'pointer',
  // Подписи на значках обычным регистром: «Готовится», а не «ГОТОВИТСЯ».
  components: { Badge: Badge.extend({ defaultProps: { tt: 'none' } }) },
});

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, staleTime: 0, refetchOnWindowFocus: true } },
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <MantineProvider theme={theme}>
      <DatesProvider settings={{ locale: 'ru', firstDayOfWeek: 1 }}>
        <Notifications position="top-right" limit={3} />
        <QueryClientProvider client={queryClient}>
          <ConfirmProvider>
            <BrowserRouter>
              <App />
            </BrowserRouter>
          </ConfirmProvider>
        </QueryClientProvider>
      </DatesProvider>
    </MantineProvider>
  </StrictMode>,
);
