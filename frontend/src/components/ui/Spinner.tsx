// frontend/src/components/ui/Spinner.tsx
import React from "react";

// className ve size prop'larını kabul eden arayüz
interface SpinnerProps {
  className?: string;
  /**
   * Küçük, orta, büyük; ayrıca string ile doğrudan Tailwind boyutu da verebilirsin.
   * Örnek: "sm" | "md" | "lg" veya "w-6 h-6"
   */
  size?: "sm" | "md" | "lg" | string;
}

const Spinner: React.FC<SpinnerProps> = ({ className = "", size = "md" }) => {
  let sizeClass = "";
  if (size === "sm") sizeClass = "w-4 h-4";
  else if (size === "md") sizeClass = "w-6 h-6";
  else if (size === "lg") sizeClass = "w-8 h-8";
  else sizeClass = String(size); // custom tailwind sınıfı verilmiş olabilir

  return (
    <svg
      className={`animate-spin ${sizeClass} ${className}`.trim()}
      xmlns="http://www.w3.org/2000/svg"
      fill="none"
      viewBox="0 0 24 24"
      aria-hidden="true"
    >
      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
      <path
        className="opacity-75"
        fill="currentColor"
        d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
      ></path>
    </svg>
  );
};

export default Spinner;
