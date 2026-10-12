import axios, { AxiosError } from "axios";
import { normalizeApiError } from "./errors";

const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL,
  timeout: 30000,
  headers: {
    "Content-Type": "application/json",
  },
  withCredentials: true,
})

let _accessToken: string | null = null;

export const setClientToken = (token: string | null) => {
  _accessToken = token;
};

api.interceptors.request.use(config => {
  if (_accessToken) {
    config.headers.Authorization = `Bearer ${_accessToken}`;
  }
  return config;
});

let isRefreshing = false;
let failedQueue: Array<{
  resolve: (token: string) => void;
  reject: (err: unknown) => void;
}> = [];

const processQueue = (token: string | null, error: unknown = null) => {
  failedQueue.forEach(({ resolve, reject }) => {
    if (error || !token) reject(error);
    else resolve(token);
  });
  failedQueue = [];
};

api.interceptors.response.use(
  response => response,
  async (error: AxiosError) => {
    const original = error.config;

    if (!original || error.response?.status !== 401) {
      return Promise.reject(normalizeApiError(error));
    }

    const isRefreshUrl = typeof original.url === "string" &&
      original.url.includes("auth/refresh");

    if (isRefreshUrl) {
      setClientToken(null);
      return Promise.reject(normalizeApiError(error));
    }

    if (isRefreshing) {
      return new Promise<string>((resolve, reject) => {
        failedQueue.push({ resolve, reject });
      }).then(token => {
        original.headers.Authorization = `Bearer ${token}`;
        return api(original);
      });
    }

    isRefreshing = true;

    try {
      const { data } = await axios.post<{ accessToken: string }>(
        `${import.meta.env.VITE_API_URL}/auth/refresh`,
        {},
        { withCredentials: true },
      );

      const newToken = data.accessToken;
      setClientToken(newToken);
      processQueue(newToken);

      original.headers.Authorization = `Bearer ${newToken}`;
      return api(original);
    } catch (refreshError) {
      processQueue(null, refreshError);
      setClientToken(null);
      return Promise.reject(normalizeApiError(error));
    } finally {
      isRefreshing = false;
    }
  },
);

export default api;
