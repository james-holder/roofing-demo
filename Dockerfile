FROM nginx:alpine

# Copy the construction page to nginx html directory
COPY construction.html /usr/share/nginx/html/index.html

# Expose port 8080 (Fly.io default)
EXPOSE 8080

# Configure nginx to listen on port 8080
RUN sed -i 's/listen\s*80;/listen 8080;/g' /etc/nginx/conf.d/default.conf

CMD ["nginx", "-g", "daemon off;"]
