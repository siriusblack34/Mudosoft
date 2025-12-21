import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ShieldCheck, User, Lock, ArrowRight, Loader2 } from 'lucide-react';

const LoginPage: React.FC = () => {
    const navigate = useNavigate();
    const [isLoading, setIsLoading] = useState(false);
    const [formData, setFormData] = useState({ username: '', password: '' });
    const [error, setError] = useState('');

    const handleLogin = (e: React.FormEvent) => {
        e.preventDefault();
        setError('');
        setIsLoading(true);

        // Simulate API delay
        setTimeout(() => {
            // Mock authentication logic
            if (formData.username && formData.password) {
                localStorage.setItem('isAuthenticated', 'true');
                navigate('/');
            } else {
                setError('Please enter valid credentials.');
                setIsLoading(false);
            }
        }, 1500);
    };

    return (
        <div className="min-h-screen bg-slate-950 flex items-center justify-center relative overflow-hidden">
            {/* Background Effects */}
            <div className="absolute top-[-20%] left-[-10%] w-[500px] h-[500px] bg-emerald-500/10 rounded-full blur-[128px]" />
            <div className="absolute bottom-[-20%] right-[-10%] w-[500px] h-[500px] bg-indigo-500/10 rounded-full blur-[128px]" />

            {/* Login Card */}
            <div className="relative w-full max-w-md p-8 bg-slate-900/40 backdrop-blur-xl border border-slate-800 rounded-2xl shadow-2xl">
                <div className="flex flex-col items-center mb-8">
                    <div className="w-16 h-16 bg-gradient-to-tr from-emerald-500 to-teal-500 rounded-2xl flex items-center justify-center shadow-lg shadow-emerald-500/20 mb-4">
                        <ShieldCheck className="w-8 h-8 text-white" />
                    </div>
                    <h1 className="text-2xl font-bold text-white tracking-tight">MudoSoft RMM</h1>
                    <p className="text-slate-400 text-sm mt-1">Secure Management Portal</p>
                </div>

                <form onSubmit={handleLogin} className="space-y-5">
                    <div className="space-y-1">
                        <label className="text-xs font-semibold text-slate-400 uppercase tracking-wider ml-1">Username</label>
                        <div className="relative group">
                            <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                                <User className="h-5 w-5 text-slate-500 group-focus-within:text-emerald-400 transition-colors" />
                            </div>
                            <input
                                type="text"
                                className="w-full bg-slate-950/50 border border-slate-700 text-slate-200 text-sm rounded-lg focus:ring-2 focus:ring-emerald-500/50 focus:border-emerald-500 block w-full pl-10 p-2.5 transition-all placeholder-slate-600"
                                placeholder="Enter your username"
                                value={formData.username}
                                onChange={(e) => setFormData({ ...formData, username: e.target.value })}
                            />
                        </div>
                    </div>

                    <div className="space-y-1">
                        <label className="text-xs font-semibold text-slate-400 uppercase tracking-wider ml-1">Password</label>
                        <div className="relative group">
                            <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                                <Lock className="h-5 w-5 text-slate-500 group-focus-within:text-emerald-400 transition-colors" />
                            </div>
                            <input
                                type="password"
                                className="w-full bg-slate-950/50 border border-slate-700 text-slate-200 text-sm rounded-lg focus:ring-2 focus:ring-emerald-500/50 focus:border-emerald-500 block w-full pl-10 p-2.5 transition-all placeholder-slate-600"
                                placeholder="••••••••"
                                value={formData.password}
                                onChange={(e) => setFormData({ ...formData, password: e.target.value })}
                            />
                        </div>
                    </div>

                    {error && (
                        <div className="text-rose-400 text-xs text-center font-medium bg-rose-500/10 py-2 rounded">
                            {error}
                        </div>
                    )}

                    <div className="flex items-center justify-between text-sm">
                        <div className="flex items-center">
                            <input id="remember-me" type="checkbox" className="w-4 h-4 text-emerald-600 bg-slate-800 border-slate-700 rounded focus:ring-emerald-500 focus:ring-offset-slate-900" />
                            <label htmlFor="remember-me" className="ml-2 text-slate-400">Remember me</label>
                        </div>
                        <a href="#" className="text-emerald-400 hover:text-emerald-300 transition-colors font-medium">Forgot password?</a>
                    </div>

                    <button
                        type="submit"
                        disabled={isLoading}
                        className="w-full flex items-center justify-center bg-gradient-to-r from-emerald-600 to-teal-600 hover:from-emerald-500 hover:to-teal-500 text-white font-semibold py-2.5 rounded-lg shadow-lg shadow-emerald-900/20 transition-all transform active:scale-95 disabled:opacity-70 disabled:cursor-not-allowed group"
                    >
                        {isLoading ? (
                            <Loader2 className="w-5 h-5 animate-spin" />
                        ) : (
                            <>
                                Sign In
                                <ArrowRight className="w-4 h-4 ml-2 group-hover:translate-x-1 transition-transform" />
                            </>
                        )}
                    </button>
                </form>

                <div className="mt-8 text-center">
                    <p className="text-slate-500 text-xs">
                        &copy; {new Date().getFullYear()} MudoSoft. All rights reserved.
                    </p>
                </div>
            </div>
        </div>
    );
};

export default LoginPage;
