import { RouterProvider, createBrowserRouter } from "react-router-dom";
import routes from "./routes";
import ErrorBoundary from "./components/common/ErrorBoundary";
import { ThemeProvider } from "./contexts/ThemeContext";
import { AuthProvider } from "./contexts/AuthContext";

function App() {
  const router = createBrowserRouter(routes);
  return (
    <ErrorBoundary>
      <ThemeProvider>
        <AuthProvider>
          <RouterProvider router={router} />
        </AuthProvider>
      </ThemeProvider>
    </ErrorBoundary>
  );
}

export default App;
