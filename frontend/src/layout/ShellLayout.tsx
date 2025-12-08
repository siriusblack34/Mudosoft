import React from 'react';
import Sidebar from './Sidebar';
import Topbar from './Topbar';

interface Props {
  children: React.ReactNode;
}

const ShellLayout: React.FC<Props> = ({ children }) => {
  return (
    <div className="min-h-screen flex bg-ms-bg text-ms-text overflow-x-hidden w-full max-w-full">
      <Sidebar />

      <div className="flex-1 flex flex-col overflow-x-hidden w-full max-w-full">
        <Topbar />

        <main
          className="
            flex-1 
            overflow-y-auto 
            overflow-x-hidden 
            px-6 py-5 
            bg-gradient-to-b 
            from-ms-bg-soft 
            to-ms-bg
            w-full 
            max-w-full
          "
        >
          {children}
        </main>
      </div>
    </div>
  );
};

export default ShellLayout;
