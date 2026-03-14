import React from 'react';
import Sidebar from './sidebar';
import Topbar from './Topbar';

interface Props {
  children: React.ReactNode;
}

const ShellLayout: React.FC<Props> = ({ children }) => {
  return (
    <div className="min-h-screen flex bg-ms-bg text-ms-text overflow-hidden w-full">
      <Sidebar />

      <div className="flex-1 flex flex-col h-screen overflow-hidden min-w-0">
        <Topbar />

        <main className="flex-1 overflow-y-auto overflow-x-hidden px-6 py-6 w-full">
          <div className="animate-fade-in">
            {children}
          </div>
        </main>
      </div>
    </div>
  );
};

export default ShellLayout;
