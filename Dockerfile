FROM maven:3.8.8-eclipse-temurin-8 AS build
WORKDIR /app
COPY erp-boot/pom.xml erp-boot/
COPY erp-boot/src erp-boot/src
RUN mvn -f erp-boot/pom.xml -DskipTests package

FROM eclipse-temurin:8-jre
WORKDIR /app
COPY --from=build /app/erp-boot/target/*.jar /app/app.jar
ENV JAVA_OPTS=""
EXPOSE 9999
ENTRYPOINT ["sh", "-c", "java $JAVA_OPTS -jar /app/app.jar"]
