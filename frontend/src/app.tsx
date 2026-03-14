// src/App.tsx

import { RouterProvider, createBrowserRouter } from "react-router-dom";
import routes from "./routes";
import ErrorBoundary from "./components/common/ErrorBoundary";

function App() {
  const router = createBrowserRouter(routes);
  return (
    <ErrorBoundary>
      <RouterProvider router={router} />
    </ErrorBoundary>
  );
}

export default App;
