import { AppShell, Badge, Burger, Group, NavLink, Text } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import {
  IconCake,
  IconCalendarEvent,
  IconChartBar,
  IconChartLine,
  IconClipboardList,
  IconPackages,
  IconSettings,
  IconUsers,
} from '@tabler/icons-react';
import type { ReactNode } from 'react';
import { Navigate, NavLink as RouterLink, Route, Routes, useLocation } from 'react-router-dom';
import { useIngredients, useOrders } from './api/hooks';
import { CustomersPage } from './pages/CustomersPage';
import { DayPage } from './pages/DayPage';
import { IngredientsPage } from './pages/IngredientsPage';
import { OrdersPage } from './pages/OrdersPage';
import { OverviewPage } from './pages/OverviewPage';
import { ProductsPage } from './pages/ProductsPage';
import { ReportsPage } from './pages/ReportsPage';
import { SettingsPage } from './pages/SettingsPage';

interface NavItem {
  to: string;
  label: string;
  icon: ReactNode;
  badge?: ReactNode;
}

export function App() {
  const [opened, { toggle, close }] = useDisclosure();
  const location = useLocation();

  // Счётчики в меню: активные заказы и продукты, которые заканчиваются.
  const activeOrders = useOrders({ scope: 'Active' }).data?.length ?? 0;
  const lowStock = useIngredients().data?.filter((i) => i.isLowStock || i.isOutOfStock).length ?? 0;

  const items: NavItem[] = [
    { to: '/day', label: 'День', icon: <IconCalendarEvent size={20} /> },
    { to: '/overview', label: 'Обзор', icon: <IconChartLine size={20} /> },
    {
      to: '/orders',
      label: 'Заказы',
      icon: <IconClipboardList size={20} />,
      badge: activeOrders > 0 && (
        <Badge size="sm" variant="light" circle>
          {activeOrders}
        </Badge>
      ),
    },
    { to: '/products', label: 'Изделия', icon: <IconCake size={20} /> },
    {
      to: '/ingredients',
      label: 'Продукты и склад',
      icon: <IconPackages size={20} />,
      badge: lowStock > 0 && (
        <Badge size="sm" color="orange" variant="light" circle title="Продукты заканчиваются">
          {lowStock}
        </Badge>
      ),
    },
    { to: '/customers', label: 'Клиенты', icon: <IconUsers size={20} /> },
    { to: '/reports', label: 'Отчёты', icon: <IconChartBar size={20} /> },
    { to: '/settings', label: 'Настройки', icon: <IconSettings size={20} /> },
  ];

  return (
    <AppShell
      header={{ height: 52, collapsed: false }}
      navbar={{ width: 250, breakpoint: 'sm', collapsed: { mobile: !opened } }}
      padding="md"
    >
      <AppShell.Header>
        <Group h="100%" px="md" gap="sm">
          <Burger opened={opened} onClick={toggle} hiddenFrom="sm" size="sm" aria-label="Меню" />
          <img src="/favicon.svg" alt="" width={26} height={26} />
          <Text fw={700} fz="lg">
            Mazzacha CRM
          </Text>
        </Group>
      </AppShell.Header>

      <AppShell.Navbar p="sm">
        {items.map((item) => (
          <NavLink
            key={item.to}
            component={RouterLink}
            to={item.to}
            label={item.label}
            leftSection={item.icon}
            rightSection={item.badge || undefined}
            active={location.pathname.startsWith(item.to)}
            onClick={close}
            className="nav-link"
            mb={4}
          />
        ))}
      </AppShell.Navbar>

      <AppShell.Main>
        <Routes>
          <Route path="/" element={<Navigate to="/day" replace />} />
          <Route path="/day/:date?" element={<DayPage />} />
          <Route path="/overview" element={<OverviewPage />} />
          <Route path="/orders" element={<OrdersPage />} />
          <Route path="/products" element={<ProductsPage />} />
          <Route path="/ingredients" element={<IngredientsPage />} />
          <Route path="/customers" element={<CustomersPage />} />
          <Route path="/reports" element={<ReportsPage />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="*" element={<Navigate to="/day" replace />} />
        </Routes>
      </AppShell.Main>
    </AppShell>
  );
}
