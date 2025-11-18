import React from 'react';
import { useRoutes } from 'react-router-dom';
import { routes } from './routes';
import ShellLayout from './layout/ShellLayout';

const App: React.FC = () => {
  const element = useRoutes(routes);

  return <ShellLayout>{element}</ShellLayout>;
};

export default App;
