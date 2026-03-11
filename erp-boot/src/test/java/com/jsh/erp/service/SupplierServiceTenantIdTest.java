package com.jsh.erp.service;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.datasource.entities.Supplier;
import com.jsh.erp.datasource.entities.SupplierExample;
import com.jsh.erp.datasource.entities.User;
import com.jsh.erp.datasource.mappers.SupplierMapper;
import com.jsh.erp.datasource.mappers.SupplierMapperEx;
import org.junit.Before;
import org.junit.Test;
import org.springframework.test.util.ReflectionTestUtils;
import org.springframework.mock.web.MockHttpServletRequest;
import org.springframework.web.context.request.RequestContextHolder;
import org.springframework.web.context.request.ServletRequestAttributes;

import javax.servlet.http.HttpServletRequest;
import java.lang.reflect.InvocationHandler;
import java.lang.reflect.Method;
import java.lang.reflect.Proxy;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

public class SupplierServiceTenantIdTest {

    private SupplierService supplierService;
    private CapturingSupplierMapperHandler supplierMapperHandler;
    private CapturingSupplierMapperHandler supplierMapperExHandler;

    @Before
    public void setup() {
        supplierMapperHandler = new CapturingSupplierMapperHandler();
        supplierMapperExHandler = new CapturingSupplierMapperHandler();

        SupplierMapper supplierMapper = (SupplierMapper) Proxy.newProxyInstance(
                SupplierMapper.class.getClassLoader(),
                new Class[]{SupplierMapper.class},
                supplierMapperHandler
        );

        SupplierMapperEx supplierMapperEx = (SupplierMapperEx) Proxy.newProxyInstance(
                SupplierMapperEx.class.getClassLoader(),
                new Class[]{SupplierMapperEx.class},
                supplierMapperExHandler
        );

        TestUserService userService = new TestUserService();
        TestLogService logService = new TestLogService();

        supplierService = new SupplierService();
        ReflectionTestUtils.setField(supplierService, "supplierMapper", supplierMapper);
        ReflectionTestUtils.setField(supplierService, "supplierMapperEx", supplierMapperEx);
        ReflectionTestUtils.setField(supplierService, "logService", logService);
        ReflectionTestUtils.setField(supplierService, "userService", userService);
    }

    @Test
    public void insertSupplier_shouldFillTenantId_whenTenantIdMissing() throws Exception {
        User user = new User();
        user.setId(11L);
        user.setTenantId(22L);
        user.setLoginName("tenantUser");
        ((TestUserService) ReflectionTestUtils.getField(supplierService, "userService")).setCurrentUser(user);

        JSONObject obj = new JSONObject();
        obj.put("type", "会员");
        obj.put("supplier", "M001");

        supplierService.insertSupplier(obj, new MockHttpServletRequest());

        assertEquals(Long.valueOf(22L), supplierMapperHandler.lastInserted.getTenantId());
    }

    @Test
    public void importExcel_shouldQueryByTenantId_andFillTenantIdOnInsert() throws Exception {
        User user = new User();
        user.setId(11L);
        user.setTenantId(22L);
        user.setLoginName("tenantUser");
        ((TestUserService) ReflectionTestUtils.getField(supplierService, "userService")).setCurrentUser(user);

        Supplier supplier = new Supplier();
        supplier.setType("会员");
        supplier.setSupplier("M002");
        supplier.setEnabled(true);
        List<Supplier> list = new ArrayList<>();
        list.add(supplier);

        MockHttpServletRequest request = new MockHttpServletRequest();
        RequestContextHolder.setRequestAttributes(new ServletRequestAttributes(request));
        try {
            supplierService.importExcel(list, "会员", request);
        } finally {
            RequestContextHolder.resetRequestAttributes();
        }

        SupplierExample example = supplierMapperHandler.lastSelectByExample;
        List<SupplierExample.Criterion> criteria = example.getOredCriteria().get(0).getAllCriteria();
        assertTrue(criteria.stream().anyMatch(c -> "tenant_id =".equals(c.getCondition()) && Long.valueOf(22L).equals(c.getValue())));

        assertEquals(Long.valueOf(22L), supplierMapperHandler.lastInserted.getTenantId());
    }

    private static class CapturingSupplierMapperHandler implements InvocationHandler {
        private Supplier lastInserted;
        private SupplierExample lastSelectByExample;

        @Override
        public Object invoke(Object proxy, Method method, Object[] args) {
            String name = method.getName();
            if ("toString".equals(name)) {
                return "CapturingSupplierMapperHandlerProxy";
            }
            if ("hashCode".equals(name)) {
                return System.identityHashCode(proxy);
            }
            if ("equals".equals(name)) {
                return proxy == (args == null ? null : args[0]);
            }
            if ("insertSelective".equals(name) && args != null && args.length == 1 && args[0] instanceof Supplier) {
                lastInserted = (Supplier) args[0];
                return 1;
            }
            if ("selectByExample".equals(name) && args != null && args.length == 1 && args[0] instanceof SupplierExample) {
                lastSelectByExample = (SupplierExample) args[0];
                return Collections.emptyList();
            }
            Class<?> returnType = method.getReturnType();
            if (returnType.equals(int.class) || returnType.equals(Integer.class)) {
                return 0;
            }
            if (returnType.equals(long.class) || returnType.equals(Long.class)) {
                return 0L;
            }
            if (returnType.equals(boolean.class) || returnType.equals(Boolean.class)) {
                return false;
            }
            if (List.class.isAssignableFrom(returnType)) {
                return Collections.emptyList();
            }
            return null;
        }
    }

    private static class TestUserService extends UserService {
        private User currentUser;

        public void setCurrentUser(User currentUser) {
            this.currentUser = currentUser;
        }

        @Override
        public User getCurrentUser() {
            return currentUser;
        }
    }

    private static class TestLogService extends LogService {
        @Override
        public void insertLog(String moduleName, String content, HttpServletRequest request) {
        }
    }
}
